using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using Newtonsoft.Json.Linq;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace JSAGROAllegroSync.Services
{
    public class AllegroApiService
    {
        private readonly AllegroAuthService _auth;
        private readonly HttpClient _http;
        private readonly IProductRepository _productRepo;

        private readonly JsonSerializerOptions options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public AllegroApiService(AllegroAuthService auth, IProductRepository productRepo, HttpClient httpClient)
        {
            _auth = auth;
            _productRepo = productRepo;
            _http = httpClient;
        }

        public async Task UpdateAllegroCategories(CancellationToken ct = default)
        {
            try
            {
                // 1. Get all products that doesnt have DefaultAllegroCategory
                var products = await _productRepo.GetProductsWithoutDefaultCategory(ct);

                // 2. Get suggested categories from [name, code, ean] and update Database
                foreach (var product in products)
                {
                    try
                    {
                        int categoryId = await GetCategoriesSuggestions(product.Name, product.CodeGaska, product.Ean, ct);
                        await _productRepo.UpdateProductAllegroCategory(product.Id, categoryId, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error while trying to fetch and update suggested Allegro category for product {product.CodeGaska} ({product.Name}).");
                    }
                }

                // 3. Get once again products that doesnt have DefaultAllegroCategory
                products = await _productRepo.GetProductsWithoutDefaultCategory(ct);

                // 4. Search database for other products that has the same main Gaska category and get calcualte the DefaultAllegroCategory from count() of ther products that has the same main Gaska category as current product
                foreach (var product in products)
                {
                    try
                    {
                        var dbCategory = await _productRepo.GetMostCommonDefaultAllegroCategory(product.Id, ct);
                        if (dbCategory.HasValue)
                        {
                            int categoryId = dbCategory.Value;
                            await _productRepo.UpdateProductAllegroCategory(product.Id, categoryId, ct);
                        }
                        else
                        {
                            Log.Warning($"No Allegro or Database category resolved for product {product.CodeGaska} ({product.Name}).");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Error while trying to fetch and update suggested database category for product {product.CodeGaska} ({product.Name}).");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in categories mapping.");
            }
        }

        private async Task<int> GetCategoriesSuggestions(string name, string code, string ean, CancellationToken ct)
        {
            int resultCategory = 0;

            try
            {
                var token = await _auth.GetAccessTokenAsync(ct);

                async Task<int> FetchCategory(string query, string queryType)
                {
                    var url = $"/sale/matching-categories?name={Uri.EscapeDataString(query)}";
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var response = await _http.SendAsync(request, ct);
                    if (!response.IsSuccessStatusCode)
                        return 0;

                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MatchingCategoriesResponse>(json, options);

                    var categories = result?.MatchingCategories;
                    if (categories == null || !categories.Any())
                        return 0;

                    // Preferred root ID
                    string preferredRootId = "99022"; // Motoryzacja/Części do maszyn i innych pojazdów

                    // Keep only categories under the preferred root
                    var candidates = categories
                        .Where(c => BuildCategoryIdPath(c).Split('/').Contains(preferredRootId))
                        .ToList();

                    if (!candidates.Any())
                    {
                        // No category under the preferred root, do nothing
                        return 0;
                    }

                    // Pick the first direct child of the preferred child ID if it exists
                    string preferredChildId = "252182"; // Części do maszyn rolniczych
                    CategoryDto preferredCategory = candidates.FirstOrDefault(c => BuildCategoryIdPath(c).Split('/').Contains(preferredChildId));

                    CategoryDto selectedCategory;

                    if (preferredCategory != null)
                    {
                        selectedCategory = candidates
                            .FirstOrDefault(c => c.Parent != null && c.Parent.Id == preferredChildId)
                            ?? preferredCategory; // fallback to preferred itself
                    }
                    else
                    {
                        // If no preferred child ther return
                        return 0;
                    }

                    Log.Information($"Suggested category for product {code}: {BuildCategoryPath(selectedCategory, c => c.Name)} ({BuildCategoryPath(selectedCategory, c => c.Id)})");
                    return Convert.ToInt32(selectedCategory.Id);
                }

                resultCategory = await FetchCategory(name, "name");
            }
            catch (Exception ex)
            {
                Log.Error($"Exception in category suggestion: {ex}");
            }

            return resultCategory;

            // Helpers
            string BuildCategoryPath(CategoryDto category, Func<CategoryDto, string> selector)
            {
                var stack = new Stack<string>();
                var current = category;
                while (current != null)
                {
                    stack.Push(selector(current));
                    current = current.Parent;
                }
                return string.Join("/", stack);
            }

            string BuildCategoryIdPath(CategoryDto category)
            {
                var stack = new Stack<string>();
                var current = category;
                while (current != null)
                {
                    stack.Push(current.Id);
                    current = current.Parent;
                }
                return string.Join("/", stack);
            }
        }

        public async Task FetchAndSaveCategoryParameters(CancellationToken ct = default)
        {
            try
            {
                var categories = await _productRepo.GetDefaultCategories(ct);

                foreach (int category in categories)
                {
                    try
                    {
                        var token = await _auth.GetAccessTokenAsync(ct);
                        var url = $"/sale/categories/{category}/parameters";
                        var request = new HttpRequestMessage(HttpMethod.Get, url);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                        var response = await _http.SendAsync(request, ct);

                        if (!response.IsSuccessStatusCode)
                        {
                            Log.Error($"Error fetching parameters for category {category}: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var result = JsonSerializer.Deserialize<CategoryParametersResponse>(json, options);

                        if (result?.Parameters == null || !result.Parameters.Any())
                        {
                            Log.Warning($"No parameters returned for category {category}");
                            continue;
                        }

                        // Map API response to Model
                        var categoryResult = result.Parameters.Select(p => new CategoryParameter
                        {
                            ParameterId = Convert.ToInt32(p.Id),
                            CategoryId = category,
                            Name = p.Name,
                            Type = p.Type,
                            Required = p.Required,
                            RequiredForProduct = p.RequiredForProduct,
                            Min = p.Restrictions?.GetMin(),
                            Max = p.Restrictions?.GetMax(),
                            AmbiguousValueId = p.Options.AmbiguousValueId,
                            CustomValuesEnabled = p.Options.CustomValuesEnabled,
                            DescribesProduct = p.Options.DescribesProduct,
                            Values = p.Dictionary?.Select(d => new CategoryParameterValue
                            {
                                Value = d.Value
                            }).ToList()
                        }).ToList();

                        await _productRepo.SaveCategoryParametersAsync(categoryResult, ct);

                        Log.Information($"Saved {categoryResult.Count} parameters for category {category}");
                    }
                    catch (Exception exProd)
                    {
                        Log.Error(exProd, $"Error fetching parameters for category {category}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error in FetchAndSaveCategoryParametersForProducts.");
            }
        }

        public async Task UpdateProductParameters(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpdateParameters(ct);

                foreach (var product in products)
                {
                    // these are tracked CategoryParameter entities from DB
                    var categoryParams = await _productRepo.GetCategoryParametersAsync(product.DefaultAllegroCategory, ct);

                    var productParams = new List<ProductParameter>();

                    foreach (var catParam in categoryParams)
                    {
                        string value = MapProductToParameter(product, catParam);

                        if (string.IsNullOrEmpty(value) && (catParam.Required || catParam.RequiredForProduct))
                        {
                            Log.Warning($"Missing required parameter {catParam.Name} for product {product.Id}");
                            continue;
                        }

                        productParams.Add(new ProductParameter
                        {
                            ProductId = product.Id,
                            CategoryParameterId = catParam.Id,
                            IsForProduct = catParam.DescribesProduct,
                            Value = value
                        });
                    }

                    await _productRepo.SaveProductParametersAsync(productParams, ct);

                    Log.Information($"Updated {productParams.Count} parameters for product {product.Id}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error updating parameters for products.");
            }
        }

        public async Task ImportImages(CancellationToken ct = default)
        {
            try
            {
                var images = await _productRepo.GetImagesForImport(ct);

                foreach (var image in images)
                {
                    try
                    {
                        var token = await _auth.GetAccessTokenAsync(ct);
                        var url = "/sale/images";

                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                        var imageResponse = await _http.GetAsync(image.Url, ct);
                        if (!imageResponse.IsSuccessStatusCode)
                        {
                            Log.Error($"Failed to download image from {image.Url} for product {image.Product.CodeGaska}");
                            continue;
                        }

                        var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();

                        request.Content = new ByteArrayContent(imageBytes);
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                        var response = await _http.SendAsync(request, ct);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        if (response.IsSuccessStatusCode)
                        {
                            var result = JsonSerializer.Deserialize<AllegroImageResponse>(responseBody, options);

                            if (result != null)
                            {
                                if (DateTime.TryParse(result.ExpiresAt, out var expiresAt))
                                {
                                    bool success = await _productRepo.UpdateProductAllegroImage(image.Id, result.Location, expiresAt, ct);

                                    if (success)
                                        Log.Information($"Image uploaded for product {image.Product.CodeGaska} ({image.Product.Name})");
                                    else
                                        Log.Error($"Couldn't save uploaded image data in database for product {image.Product.CodeGaska} ({image.Product.Name})");
                                }
                                else
                                {
                                    Log.Warning($"Couldn't parse Allegro expiration date for product {image.Product.CodeGaska} ({image.Product.Name})");
                                }
                            }
                        }
                        else
                        {
                            Log.Error($"Failed to upload image {image.Product.CodeGaska} ({image.Product.Name}. Response: {responseBody}");
                        }
                    }
                    catch (Exception exImage)
                    {
                        Log.Error(exImage, $"Exception while uploading image for product {image.Product.CodeGaska} ({image.Product.Name}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while importing images to Allegro.");
            }
        }

        public async Task FetchAndSaveCompatibleProducts(CancellationToken ct = default)
        {
            try
            {
                var types = new[] { "TRACTOR" };

                foreach (var type in types)
                {
                    var groups = await GetCompatibleProductGroups(type, ct);

                    foreach (var group in groups)
                    {
                        var products = await GetCompatibilityList(type, group.Id, ct);

                        var productEntities = products.Select(p => new CompatibleProduct
                        {
                            Id = p.Id,
                            Name = p.Text,
                            Type = type,
                            GroupName = group.Text
                        }).ToList();

                        await _productRepo.SaveCompatibleProductsAsync(productEntities, ct);

                        Log.Information("💾 Saved {Count} products for type={Type}, group={GroupId}", productEntities.Count, type, group.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Fatal error while fetching and saving compatible products.");
            }
        }

        private async Task<List<CompatibleGroupDto>> GetCompatibleProductGroups(string type, CancellationToken ct = default)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            var url = $"/sale/compatible-products/groups?type={type}&limit=200&offset=0";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

            var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("❌ Failed to get groups for type {Type}: Status={Status}, Body={Body}", type, response.StatusCode, body);
                return new List<CompatibleGroupDto>();
            }

            try
            {
                var groupsResponse = JsonSerializer.Deserialize<CompatibleProductGroupsResponse>(body, options);
                return groupsResponse.Groups;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "❌ Failed to parse groups JSON for type {Type}", type);
                return new List<CompatibleGroupDto>();
            }
        }

        private async Task<List<CompatibleProductDto>> GetCompatibilityList(string type, string groupId, CancellationToken ct = default)
        {
            var allProducts = new List<CompatibleProductDto>();
            var token = await _auth.GetAccessTokenAsync(ct);

            int limit = 200;
            int offset = 0;

            while (true)
            {
                var url = $"/sale/compatible-products?type={type}&group.id={groupId}&limit={limit}&offset={offset}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                var response = await _http.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("❌ Failed to get products for type={Type}, group={GroupId}, offset={Offset}: Status={Status}, Body={Body}",
                        type, groupId, offset, response.StatusCode, body);
                    break; // stop fetching if request failed
                }

                try
                {
                    var compatResponse = JsonSerializer.Deserialize<CompatibleProductsResponse>(body, options);
                    var products = compatResponse?.CompatibleProducts ?? new List<CompatibleProductDto>();

                    if (!products.Any())
                        break; // no more data → exit loop

                    allProducts.AddRange(products);

                    Log.Information("📥 Fetched {Count} products for type={Type}, group={GroupId}, offset={Offset}", products.Count, type, groupId, offset);
                    foreach (var prod in products)
                    {
                        foreach (var app in prod.Attributes)
                        {
                            foreach (var val in app.Values)
                            {
                                Log.Debug("{product} - {AttrId}: {Value}", prod.Text, app.Id, val);
                            }
                        }
                    }

                    offset += limit; // move to next page
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "❌ Failed to parse products JSON for type={Type}, group={GroupId}, offset={Offset}", type, groupId, offset);
                    break;
                }
            }

            return allProducts;
        }

        public async Task CreateOffers(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpload(ct);

                foreach (var product in products)
                {
                    try
                    {
                        var token = await _auth.GetAccessTokenAsync(ct);
                        var url = "/sale/product-offers";
                        var request = new HttpRequestMessage(HttpMethod.Post, url);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));

                        var offer = OfferFactory.BuildOffer(product);
                        var jsonContent = JsonSerializer.Serialize(offer, options);
                        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.allegro.public.v1+json");

                        var response = await _http.SendAsync(request, ct);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        await LogAllegroResponse(product, response, responseBody);
                    }
                    catch (Exception exProd)
                    {
                        Log.Error(exProd, $"❌ Exception while creating offer for {product.Name} ({product.CodeGaska})");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "🔥 Fatal error while creating Allegro offers from products.");
            }
        }

        /// <summary>
        /// Logs Allegro API responses consistently.
        /// </summary>
        private async Task LogAllegroResponse(Product product, HttpResponseMessage response, string body)
        {
            switch ((int)response.StatusCode)
            {
                case 201:
                    Log.Information($"✅ Offer created successfully for {product.Name} ({product.CodeGaska})");
                    break;

                case 202:
                    Log.Information($"⌛ Offer creation accepted but still processing for {product.Name} ({product.CodeGaska})");
                    break;

                case 400:
                case 422:
                case 433:
                    await LogAllegroErrors(product, response, body);
                    break;

                case 401:
                    Log.Error($"🔒 Unauthorized (401). Check token for product {product.CodeGaska}.");
                    break;

                case 403:
                    Log.Error($"🚫 Forbidden (403). No permission to create offer for {product.CodeGaska}.");
                    break;

                default:
                    Log.Error($"⚠️ Unexpected status {(int)response.StatusCode} ({response.StatusCode}) for {product.CodeGaska}. Response: {body}");
                    break;
            }
        }

        /// <summary>
        /// Parses and logs Allegro API error details.
        /// </summary>
        private async Task LogAllegroErrors(Product product, HttpResponseMessage response, string body)
        {
            try
            {
                var errorResponse = JsonSerializer.Deserialize<AllegroErrorResponse>(body, options);
                if (errorResponse?.Errors != null)
                {
                    foreach (var err in errorResponse.Errors)
                    {
                        Log.Error($"❌ Offer error for {product.CodeGaska}: " +
                                  $"Code={err.Code}, Message={err.Message}, " +
                                  $"UserMessage={err.UserMessage ?? "N/A"}, Path={err.Path ?? "N/A"}, Details={err.Details ?? "N/A"}");

                        // Special handling for Allegro 433 Validation errors
                        if ((int)response.StatusCode == 433 && err.Metadata != null && err.Metadata.Any())
                        {
                            foreach (var kv in err.Metadata)
                            {
                                Log.Warning($"   ↳ Metadata: {kv.Key} = {kv.Value}");
                            }
                        }
                        else if (err.Metadata != null && err.Metadata.Any())
                        {
                            var metaJson = JsonSerializer.Serialize(err.Metadata);
                            Log.Warning($"   ↳ Metadata: {metaJson}");
                        }
                    }
                }
                else
                {
                    Log.Error($"❌ Offer error {response.StatusCode} for {product.CodeGaska}: {body}");
                }
            }
            catch (Exception exParse)
            {
                Log.Error(exParse, $"Failed to parse Allegro error ({response.StatusCode}) for {product.CodeGaska}. Body={body}");
            }
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var lowered = value.Trim().ToLowerInvariant();
            return lowered == "rollon-solid" ? "rollon" : lowered;
        }

        private string MapProductToParameter(Product product, CategoryParameter param)
        {
            if (param == null) return null;

            var name = param.Name?.ToLowerInvariant();

            var directMappings = new Dictionary<string, Func<Product, string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["stan"] = _ => "Nowy",
                ["waga produktu z opakowaniem jednostkowym"] = p => p.WeightNet.ToString(), // bez ?. dla float
                //["ean (gtin)"] = p => p.Ean,
                ["numer katalogowy części"] = p => p.CodeGaska,
                ["numer katalogowy oryginału"] = p => p.CodeGaska,
                ["numery katalogowe zamienników"] = p => p.CrossNumbers != null ? string.Join(",", p.CrossNumbers.Select(cn => cn.CrossNumberValue)) : null,
                ["stan opakowania"] = _ => "oryginalne",
                ["jakość części (zgodnie z gvo)"] = _ => "P - zamiennik o jakości porównywalnej do oryginału"
            };

            Func<Product, string> resolver;
            if (name != null && directMappings.TryGetValue(name, out resolver))
                return resolver(product);

            if (name == "producent" || name == "producent części")
                return GetMatchingValue(product, param);

            if (name == "marka" || name == "marka maszyny")
                return GetBrandMatchingValue(product, param);

            return null;
        }

        private string GetMatchingValue(Product product, CategoryParameter param)
        {
            const string fallback = "inny";

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true
                && param.Values?.Any() == true)
            {
                var dict = param.Values
                    .Where(v => !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => new { Raw = v.Value, Normalized = Normalize(v.Value) })
                    .ToList();

                var dictSet = new HashSet<string>(dict.Select(d => d.Normalized));

                // 1. SupplierName exact match
                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplier = Normalize(product.SupplierName);
                    var match = dict.FirstOrDefault(v => v.Normalized == supplier);
                    if (match != null) return match.Raw;
                }

                // 2. Words from Product.Name
                if (!string.IsNullOrEmpty(product.Name))
                {
                    var words = product.Name
                        .Split(new[] { ' ', '-', '/', '_' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(Normalize)
                        .Where(w => !string.IsNullOrEmpty(w));

                    foreach (var word in words)
                    {
                        if (dictSet.Contains(word))
                            return dict.First(v => v.Normalized == word).Raw;

                        var contains = dict.FirstOrDefault(v =>
                            (" " + v.Normalized + " ").Contains(" " + word + " "));
                        if (contains != null) return contains.Raw;
                    }
                }

                return fallback;
            }

            return !string.IsNullOrEmpty(product.SupplierName)
                ? Normalize(product.SupplierName)
                : fallback;
        }

        private string GetBrandMatchingValue(Product product, CategoryParameter param)
        {
            const string fallback = "inny";

            if (product.Applications == null || !product.Applications.Any())
                return fallback;

            var rootBrands = product.Applications
                .Where(a => a.ParentID == 0)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (!rootBrands.Any())
                return fallback;

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true
                && param.Values?.Any() == true)
            {
                var dictValues = param.Values.Select(v => v.Value).ToList();

                var brandMatch = dictValues.FirstOrDefault(dv =>
                    rootBrands.Any(rb => dv.Equals(rb, StringComparison.OrdinalIgnoreCase)));

                if (!string.IsNullOrEmpty(brandMatch)) return brandMatch;

                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplierMatch = dictValues.FirstOrDefault(dv =>
                        dv.Equals(product.SupplierName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(supplierMatch)) return supplierMatch;
                }
            }

            return product.SupplierName ?? rootBrands.FirstOrDefault() ?? fallback;
        }

        //public async Task<string> GenerateXMLFile()
        //{
        //    string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result");
        //    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        //    string resultFileName = $"products_{timestamp}.xml";
        //    string resultFilePath = Path.Combine(resultPath, resultFileName);
        //    List<Product> products = new List<Product>();

        //    // Clean up old files
        //    CleanupOldXmlFiles();

        //    try
        //    {
        //        if (!Directory.Exists(resultPath))
        //        {
        //            Directory.CreateDirectory(resultPath);
        //        }

        //        using (var db = new MyDbContext())
        //        {
        //            products = await db.Products
        //                .Include(p => p.Categories)
        //                .Include(p => p.Applications)
        //                .Include(p => p.Parameters)
        //                .Include(p => p.CrossNumbers)
        //                .Include(p => p.Packages)
        //                .Include(p => p.RecommendedParts)
        //                .Include(p => p.Components)
        //                .Include(p => p.Files)
        //                .Include(p => p.Images)
        //                .Where(p => p.Categories.Any() && p.Archived == false)
        //                .ToListAsync();
        //        }

        //        if (products.Any())
        //        {
        //            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };

        //            using (XmlWriter writer = XmlWriter.Create(resultFilePath, settings))
        //            {
        //                writer.WriteStartElement("Products");
        //                foreach (var product in products)
        //                {
        //                    writer.WriteStartElement("Product");

        //                    WriteRawElement(writer, "Id", product.Id.ToString());
        //                    WriteRawElement(writer, "CodeGaska", product.CodeGaska);
        //                    WriteRawElement(writer, "Name", product.Name);
        //                    if (!string.IsNullOrEmpty(product.SupplierName))
        //                        WriteRawElement(writer, "Supplier", product.SupplierName);

        //                    WriteRawElement(writer, "EAN", product.Ean);
        //                    WriteRawElement(writer, "WeightNet", product.WeightNet.ToString());
        //                    WriteRawElement(writer, "WeightGross", product.WeightGross.ToString());
        //                    WriteRawElement(writer, "Stock", product.InStock.ToString());
        //                    WriteRawElement(writer, "PriceNet", product.PriceNet.ToString());
        //                    WriteRawElement(writer, "PriceGross", product.PriceGross.ToString());

        //                    // Prepare the HTML description
        //                    var descriptionBuilder = new StringBuilder();
        //                    if (!string.IsNullOrEmpty(product.Description))
        //                    {
        //                        descriptionBuilder.Append("<p><b>Opis: </b>");
        //                        descriptionBuilder.Append(product.Description);
        //                        descriptionBuilder.Append("</p>");
        //                    }

        //                    if (!string.IsNullOrEmpty(product.TechnicalDetails))
        //                    {
        //                        descriptionBuilder.Append("<p><b>Porady techniczne: </b>");
        //                        descriptionBuilder.Append(product.TechnicalDetails);
        //                        descriptionBuilder.Append("</p>");
        //                    }

        //                    if (product.Parameters != null && product.Parameters.Any())
        //                    {
        //                        descriptionBuilder.Append("<p><b>Parametry: </b>");
        //                        descriptionBuilder.Append(string.Join(", ", product.Parameters?.Select(param => $"{param.AttributeName} : {param.AttributeValue}") ?? new List<string>()));
        //                        descriptionBuilder.Append("</p>");
        //                    }

        //                    if (product.CrossNumbers != null && product.CrossNumbers.Any())
        //                    {
        //                        descriptionBuilder.Append("<p><b>Numery referencyjne: </b>");
        //                        descriptionBuilder.Append(string.Join(", ", product.CrossNumbers.Select(c => c.CrossNumberValue)));
        //                        descriptionBuilder.Append("</p>");
        //                    }

        //                    if (product.Applications != null && product.Applications.Any())
        //                    {
        //                        // Group applications by ParentID to build a tree
        //                        var applicationsByParent = product.Applications
        //                            .GroupBy(a => a.ParentID)
        //                            .ToDictionary(g => g.Key, g => g.ToList());

        //                        string BuildApplicationHtmlList(List<Application> apps, int depth = 1)
        //                        {
        //                            if (apps == null || !apps.Any())
        //                                return string.Empty;

        //                            var sb = new StringBuilder();
        //                            sb.Append("<ul>");

        //                            if (depth >= 3)
        //                            {
        //                                var nodesWithChildren = apps.Where(a => applicationsByParent.ContainsKey(a.ApplicationId)).ToList();
        //                                var leafNodes = apps.Where(a => !applicationsByParent.ContainsKey(a.ApplicationId)).ToList();

        //                                // Check if all siblings are leaves (no siblings with children)
        //                                bool allSiblingsAreLeaves = !apps.Any(a => applicationsByParent.ContainsKey(a.ApplicationId));

        //                                // Render nodes with children first
        //                                foreach (var app in nodesWithChildren)
        //                                {
        //                                    sb.Append("<li>");
        //                                    sb.Append(app.Name);
        //                                    sb.Append(BuildApplicationHtmlList(applicationsByParent[app.ApplicationId], depth + 1));
        //                                    sb.Append("</li>");
        //                                }

        //                                if (leafNodes.Any())
        //                                {
        //                                    if (allSiblingsAreLeaves)
        //                                    {
        //                                        // All siblings are leaves — group them
        //                                        sb.Append("<li>");
        //                                        sb.Append(string.Join(", ", leafNodes.Select(a => a.Name)));
        //                                        sb.Append("</li>");
        //                                    }
        //                                    else
        //                                    {
        //                                        // Mixed siblings — output each leaf separately
        //                                        foreach (var app in leafNodes)
        //                                        {
        //                                            sb.Append("<li>");
        //                                            sb.Append(app.Name);
        //                                            sb.Append("</li>");
        //                                        }
        //                                    }
        //                                }
        //                            }
        //                            else
        //                            {
        //                                // depth 1 or 2: no grouping - all separate <li>
        //                                foreach (var app in apps)
        //                                {
        //                                    sb.Append("<li>");
        //                                    sb.Append(app.Name);

        //                                    if (applicationsByParent.ContainsKey(app.ApplicationId))
        //                                        sb.Append(BuildApplicationHtmlList(applicationsByParent[app.ApplicationId], depth + 1));

        //                                    sb.Append("</li>");
        //                                }
        //                            }

        //                            sb.Append("</ul>");
        //                            return sb.ToString();
        //                        }

        //                        if (applicationsByParent.ContainsKey(0))
        //                        {
        //                            var rootApplications = applicationsByParent[0];

        //                            descriptionBuilder.Append("<div class=\"product-applications\">");
        //                            descriptionBuilder.Append("<p><b>Zastosowanie: </b></p>");
        //                            descriptionBuilder.Append(BuildApplicationHtmlList(rootApplications));
        //                            descriptionBuilder.Append("</div>");
        //                        }
        //                    }

        //                    WriteRawElement(writer, "Description", descriptionBuilder.ToString());

        //                    // Images
        //                    if (product.Images != null && product.Images.Any())
        //                    {
        //                        writer.WriteStartElement("Images");
        //                        foreach (var img in product.Images)
        //                        {
        //                            writer.WriteStartElement("Image");
        //                            WriteRawElement(writer, "ImageTitle", img.Title);
        //                            WriteRawElement(writer, "ImageUrl", img.Url);
        //                            writer.WriteEndElement(); // Image
        //                        }
        //                        writer.WriteEndElement(); // Images
        //                    }

        //                    // Packages
        //                    if (product.Packages != null && product.Packages.Any())
        //                    {
        //                        writer.WriteStartElement("Packages");
        //                        foreach (var pack in product.Packages)
        //                        {
        //                            writer.WriteStartElement("Package");
        //                            WriteRawElement(writer, "PackUnit", pack.PackUnit);
        //                            WriteRawElement(writer, "PackQty", pack.PackQty.ToString());
        //                            WriteRawElement(writer, "PackNettWeight", pack.PackNettWeight.ToString());
        //                            WriteRawElement(writer, "PackGrossWeight", pack.PackGrossWeight.ToString());
        //                            WriteRawElement(writer, "PackEan", pack.PackEan);
        //                            WriteRawElement(writer, "PackRequired", pack.PackRequired.ToString());
        //                            writer.WriteEndElement(); // Package
        //                        }
        //                        writer.WriteEndElement(); // Packages
        //                    }

        //                    // Components
        //                    if (product.Components != null && product.Components.Any())
        //                    {
        //                        writer.WriteStartElement("Components");
        //                        foreach (var comp in product.Components)
        //                        {
        //                            writer.WriteStartElement("Component");
        //                            WriteRawElement(writer, "TwrID", comp.TwrID.ToString());
        //                            WriteRawElement(writer, "CodeGaska", comp.CodeGaska);
        //                            WriteRawElement(writer, "Qty", comp.Qty.ToString());
        //                            writer.WriteEndElement(); // Component
        //                        }
        //                        writer.WriteEndElement(); // Components
        //                    }

        //                    // Parameters
        //                    if (product.Parameters != null && product.Parameters.Any())
        //                    {
        //                        writer.WriteStartElement("Parameters");
        //                        foreach (var param in product.Parameters)
        //                        {
        //                            writer.WriteStartElement("Parameter");
        //                            WriteRawElement(writer, "AttributeName", param.AttributeName);
        //                            WriteRawElement(writer, "AttributeValue", param.AttributeValue);
        //                            writer.WriteEndElement(); // Parameter
        //                        }
        //                        writer.WriteEndElement(); // Parameters
        //                    }

        //                    // RecommendedParts
        //                    if (product.RecommendedParts != null && product.RecommendedParts.Any())
        //                    {
        //                        writer.WriteStartElement("RecommendedParts");
        //                        foreach (var rec in product.RecommendedParts)
        //                        {
        //                            writer.WriteStartElement("RecommendedPart");
        //                            WriteRawElement(writer, "TwrId", rec.TwrID.ToString());
        //                            WriteRawElement(writer, "CodeGaska", rec.CodeGaska);
        //                            WriteRawElement(writer, "Qty", rec.Qty.ToString());
        //                            writer.WriteEndElement(); // RecommendedPart
        //                        }
        //                        writer.WriteEndElement(); // RecommendedParts
        //                    }

        //                    writer.WriteEndElement(); // Close Product
        //                }
        //                writer.WriteEndElement(); // Close Products
        //            }

        //            Log.Information($"Generated XML file in {resultFilePath}");
        //        }
        //        else
        //        {
        //            Log.Warning("Database is empty. No products to send.");
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "Error while making XML file."); ;
        //    }

        //    return resultFilePath;
        //}

        //public async Task UploadFileToFtp(string localFilePath)
        //{
        //    try
        //    {
        //        string ftpUri = $"ftp://{_allegroSettings.Ip}:{_allegroSettings.Port}/products.xml";
        //        FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUri);
        //        request.Method = WebRequestMethods.Ftp.UploadFile;
        //        request.Credentials = new NetworkCredential(_allegroSettings.Username, _allegroSettings.Password);

        //        byte[] fileContents = File.ReadAllBytes(localFilePath);
        //        request.ContentLength = fileContents.Length;

        //        using (Stream requestStream = await request.GetRequestStreamAsync())
        //        {
        //            await requestStream.WriteAsync(fileContents, 0, fileContents.Length);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Log.Error(ex, "Error uploading file to FTP.");
        //    }
        //}
    }
}