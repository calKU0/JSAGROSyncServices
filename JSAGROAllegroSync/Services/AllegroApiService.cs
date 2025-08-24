using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using Serilog;
using Serilog.Core;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Policy;
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
                // 1. Get all products that don't have DefaultAllegroCategory
                var products = await _productRepo.GetProductsWithoutDefaultCategory(ct);
                if (!products.Any()) return;

                // 2. Fetch suggested categories from API in parallel
                var categoryResults = new List<(int ProductId, int CategoryId, string CodeGaska, string Name)>();
                foreach (var product in products)
                {
                    try
                    {
                        int categoryId = await GetCategoriesSuggestions(product, ct);
                        categoryResults.Add((product.Id, categoryId, product.CodeGaska, product.Name));
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error while trying to fetch suggested Allegro category for product {CodeGaska} ({Name}).",
                            product.CodeGaska, product.Name);
                    }
                }

                // 3. Update DB sequentially
                foreach (var result in categoryResults)
                {
                    try
                    {
                        await _productRepo.UpdateProductAllegroCategory(result.ProductId, result.CategoryId, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error updating Allegro category in DB for product {CodeGaska} ({Name}).",
                            result.CodeGaska, result.Name);
                    }
                }

                // 4. Get once again products that don't have DefaultAllegroCategory
                products = await _productRepo.GetProductsWithoutDefaultCategory(ct);

                // 5. Sequentially fetch DB-based categories and update
                foreach (var product in products)
                {
                    try
                    {
                        var dbCategory = await _productRepo.GetMostCommonDefaultAllegroCategory(product.Id, ct);
                        if (dbCategory.HasValue)
                        {
                            await _productRepo.UpdateProductAllegroCategory(product.Id, dbCategory.Value, ct);
                        }
                        else
                        {
                            Log.Warning("No Allegro or Database category resolved for product {CodeGaska} ({Name}).",
                                product.CodeGaska, product.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error while updating suggested database category for product {CodeGaska} ({Name}).",
                            product.CodeGaska, product.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in categories mapping.");
            }
        }

        private async Task<int> GetCategoriesSuggestions(Product product, CancellationToken ct)
        {
            try
            {
                // Determine next preferred child based on 2nd-level applications
                string nextPreferredChildId = "252202"; // Default
                if (product.Applications != null && product.Applications.Any(a => a.ParentID != 0))
                {
                    if (product.Applications.Any(a => a.Name != null &&
                                                      a.Name.IndexOf("kombajn", StringComparison.OrdinalIgnoreCase) >= 0))
                        nextPreferredChildId = "319108";
                    else if (product.Applications.Any(a => a.Name != null &&
                                                           a.Name.IndexOf("Ciągnik", StringComparison.OrdinalIgnoreCase) >= 0))
                        nextPreferredChildId = "252204";
                }

                string url = $"/sale/matching-categories?name={product.Name}";
                var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, url, ct);
                var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode) return 0;

                var result = await DeserializeResponseAsync<MatchingCategoriesResponse>(response);
                var categories = result?.MatchingCategories;
                if (categories == null || !categories.Any()) return 0;

                const string preferredRootId = "99022";
                const string preferredRootChildId = "252182";

                var candidatesUnderRoot = categories
                    .Where(c => BuildCategoryIdPath(c).Split('/').Contains(preferredRootId))
                    .ToList();
                if (!candidatesUnderRoot.Any()) return 0;

                var candidatesUnderRootChild = candidatesUnderRoot
                    .Where(c => BuildCategoryIdPath(c).Split('/').Contains(preferredRootChildId))
                    .ToList();
                if (!candidatesUnderRootChild.Any()) return 0;

                var selectedCategory = candidatesUnderRootChild.FirstOrDefault(c => BuildCategoryIdPath(c).Split('/').Contains(nextPreferredChildId));

                if (selectedCategory == null && nextPreferredChildId == "319108")
                {
                    // Secondary fallback to 319159
                    const string secondaryFallbackId = "319159";
                    selectedCategory = candidatesUnderRootChild.FirstOrDefault(c => BuildCategoryIdPath(c).Split('/').Contains(secondaryFallbackId));
                }

                // Ultimate fallback
                if (selectedCategory == null)
                {
                    selectedCategory = candidatesUnderRootChild.First();
                }

                // Sequential DbContext call
                await _productRepo.SaveCategoryTreeAsync(selectedCategory, ct);

                Log.Information("Selected category for product {CodeGaska}: {Path}",
                    product.CodeGaska, BuildCategoryPath(selectedCategory, c => c.Name));

                return Convert.ToInt32(selectedCategory.Id);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception in category suggestion for product {CodeGaska}", product.CodeGaska);
                return 0;
            }

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

                foreach (var category in categories) // sequential
                {
                    try
                    {
                        var url = $"/sale/categories/{category}/parameters";
                        var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, url, ct);
                        var response = await _http.SendAsync(request, ct);

                        if (!response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            Log.Error("Error fetching parameters for category {Category}: {Status}, {Content}",
                                category, response.StatusCode, content);
                            continue;
                        }

                        var result = await DeserializeResponseAsync<CategoryParametersResponse>(response);

                        if (result?.Parameters == null || !result.Parameters.Any())
                        {
                            Log.Warning("No parameters returned for category {Category}", category);
                            continue;
                        }

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

                        Log.Information("Saved {Count} parameters for category {Category}", categoryResult.Count, category);
                    }
                    catch (Exception exCategory)
                    {
                        Log.Error(exCategory, "Error fetching parameters for category {Category}", category);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error in FetchAndSaveCategoryParameters.");
            }
        }

        public async Task UpdateProductParameters(CancellationToken ct = default)
        {
            try
            {
                var products = await _productRepo.GetProductsToUpdateParameters(ct);

                foreach (var product in products) // sequential
                {
                    try
                    {
                        var categoryParams = await _productRepo.GetCategoryParametersAsync(product.DefaultAllegroCategory, ct);
                        var productParams = new List<ProductParameter>();

                        foreach (var catParam in categoryParams)
                        {
                            string value = MapProductToParameter(product, catParam);

                            if (string.IsNullOrEmpty(value) && (catParam.Required || catParam.RequiredForProduct))
                            {
                                Log.Warning("Missing required parameter {ParamName} for product {ProductId}", catParam.Name, product.Id);
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
                        Log.Information("Updated {Count} parameters for product {ProductId}", productParams.Count, product.Id);
                    }
                    catch (Exception exProduct)
                    {
                        Log.Error(exProduct, "Error updating parameters for product {ProductId}", product.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error updating parameters for products.");
            }
        }

        public async Task ImportImages(CancellationToken ct = default)
        {
            try
            {
                var images = await _productRepo.GetImagesForImport(ct);

                foreach (var image in images) // sequential
                {
                    try
                    {
                        var imageResponse = await _http.GetAsync(image.Url, ct);
                        if (!imageResponse.IsSuccessStatusCode)
                        {
                            Log.Error("Failed to download image from {Url} for product {CodeGaska}", image.Url, image.Product.CodeGaska);
                            continue;
                        }

                        var imageBytes = await imageResponse.Content.ReadAsByteArrayAsync();
                        var ms = new MemoryStream(imageBytes);
                        var bitmap = new Bitmap(ms);
                        if (bitmap.Width < 400 && bitmap.Height < 400)
                        {
                            Log.Information("Skipping image for product {CodeGaska} ({Name}) because it is too small: {Width}x{Height}px", image.Product.CodeGaska, image.Product.Name, bitmap.Width, bitmap.Height);
                            continue;
                        }

                        var url = "/sale/images";
                        var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, url, ct);
                        request.Content = new ByteArrayContent(imageBytes);
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

                        var response = await _http.SendAsync(request, ct);
                        if (!response.IsSuccessStatusCode)
                        {
                            var body = await response.Content.ReadAsStringAsync();
                            Log.Error("Failed to upload image {CodeGaska} ({Name}). Status={Status}, Body={Body}", image.Product.CodeGaska, image.Product.Name, response.StatusCode, body);
                            continue;
                        }

                        var result = await DeserializeResponseAsync<AllegroImageResponse>(response);
                        if (result == null || !DateTime.TryParse(result.ExpiresAt, out var expiresAt))
                        {
                            Log.Warning("Couldn't parse Allegro expiration date for product {CodeGaska} ({Name})",
                                image.Product.CodeGaska, image.Product.Name);
                            continue;
                        }

                        bool success = await _productRepo.UpdateProductAllegroImage(image.Id, result.Location, expiresAt, ct);
                        if (success)
                            Log.Information("Image uploaded for product {CodeGaska} ({Name})", image.Product.CodeGaska, image.Product.Name);
                        else
                            Log.Error("Couldn't save uploaded image data in database for product {CodeGaska} ({Name})",
                                image.Product.CodeGaska, image.Product.Name);
                    }
                    catch (Exception exImage)
                    {
                        Log.Error(exImage, "Exception while uploading image for product {CodeGaska} ({Name})",
                            image.Product.CodeGaska, image.Product.Name);
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
                        try
                        {
                            var products = await GetCompatibilityList(type, group.Id, ct);

                            var productEntities = products.Select(p => new CompatibleProduct
                            {
                                Id = p.Id,
                                Name = p.Attributes
                                    .Where(a => a.Id == "MODEL")
                                    .Select(a => string.Join(",", a.Values))
                                    .FirstOrDefault(),
                                Type = type,
                                GroupName = group.Text
                            }).ToList();

                            await _productRepo.SaveCompatibleProductsAsync(productEntities, ct);
                            Log.Information("Saved {Count} products for type={Type}, group={GroupId}", productEntities.Count, type, group.Id);
                        }
                        catch (Exception exGroup)
                        {
                            Log.Error(exGroup, "Error processing group {GroupId} for type {Type}", group.Id, type);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while fetching and saving compatible products.");
            }
        }

        private async Task<List<CompatibleGroupDto>> GetCompatibleProductGroups(string type, CancellationToken ct = default)
        {
            try
            {
                var url = $"/sale/compatible-products/groups?type={type}&limit=200&offset=0";

                var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, url, ct);
                var response = await _http.SendAsync(request, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to get groups for type {Type}: Status={Status}, Body={Body}", type, response.StatusCode, body);
                    return new List<CompatibleGroupDto>();
                }

                var groupsResponse = await DeserializeResponseAsync<CompatibleProductGroupsResponse>(response);
                return groupsResponse?.Groups ?? new List<CompatibleGroupDto>();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch compatible product groups for type {Type}", type);
                return new List<CompatibleGroupDto>();
            }
        }

        private async Task<List<CompatibleProductDto>> GetCompatibilityList(string type, string groupId, CancellationToken ct = default)
        {
            var allProducts = new List<CompatibleProductDto>();
            int limit = 200;
            int offset = 0;

            while (true)
            {
                try
                {
                    var url = $"/sale/compatible-products?type={type}&group.id={groupId}&limit={limit}&offset={offset}";
                    var request = await CreateAuthorizedRequestAsync(HttpMethod.Get, url, ct);
                    var response = await _http.SendAsync(request, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        var body = await response.Content.ReadAsStringAsync();
                        Log.Error("Failed to get products for type={Type}, group={GroupId}, offset={Offset}: Status={Status}, Body={Body}",
                            type, groupId, offset, response.StatusCode, body);
                        break;
                    }

                    var compatResponse = await DeserializeResponseAsync<CompatibleProductsResponse>(response);
                    var products = compatResponse?.CompatibleProducts ?? new List<CompatibleProductDto>();

                    if (!products.Any())
                        break;

                    allProducts.AddRange(products);
                    offset += limit;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to fetch compatibility list for type={Type}, group={GroupId}, offset={Offset}", type, groupId, offset);
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
                var compatibleProducts = await _productRepo.GetCompatibilityList(ct);
                var allegroCategories = await _productRepo.GetAllegroCategories(ct);

                foreach (var product in products)
                {
                    try
                    {
                        var url = "/sale/product-offers";
                        var request = await CreateAuthorizedRequestAsync(HttpMethod.Post, url, ct);

                        var offer = OfferFactory.BuildOffer(product, compatibleProducts, allegroCategories);
                        var jsonContent = JsonSerializer.Serialize(offer, options);

                        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/vnd.allegro.public.v1+json");

                        var response = await _http.SendAsync(request, ct);
                        var responseBody = await response.Content.ReadAsStringAsync();

                        await LogAllegroResponse(product, response, responseBody);
                    }
                    catch (Exception exProd)
                    {
                        Log.Error(exProd, "Exception while creating offer for product {CodeGaska}, ({Name})", product.CodeGaska, product.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fatal error while creating Allegro offers from products.");
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
                    Log.Information($"Offer created successfully for {product.Name} ({product.CodeGaska})");
                    break;

                case 202:
                    Log.Information($"Offer creation accepted but still processing for {product.Name} ({product.CodeGaska})");
                    break;

                case 400:
                case 422:
                case 433:
                    await LogAllegroErrors(product, response, body);
                    break;

                case 401:
                    Log.Error($"Unauthorized (401). Check token for product {product.CodeGaska}.");
                    break;

                case 403:
                    Log.Error($"Forbidden (403). No permission to create offer for {product.CodeGaska}.");
                    break;

                default:
                    Log.Error($"Unexpected status {(int)response.StatusCode} ({response.StatusCode}) for {product.CodeGaska}. Response: {body}");
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
            const string fallback = "Inna";

            if (product.Applications == null || !product.Applications.Any())
                return fallback;

            var rootBrands = product.Applications
                .Where(a => a.ParentID == 0)
                .Select(a => a.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            if (!rootBrands.Any())
                return fallback;

            if (param?.Type?.Equals("dictionary", StringComparison.OrdinalIgnoreCase) == true && param.Values?.Any() == true)
            {
                var dictValues = param.Values.Select(v => v.Value).ToList();

                var brandMatch = dictValues.FirstOrDefault(dv => rootBrands.Any(rb => dv.Equals(rb, StringComparison.OrdinalIgnoreCase)));

                if (!string.IsNullOrEmpty(brandMatch)) return brandMatch;

                if (!string.IsNullOrEmpty(product.SupplierName))
                {
                    var supplierMatch = dictValues.FirstOrDefault(dv => dv.Equals(product.SupplierName, StringComparison.OrdinalIgnoreCase));
                    if (!string.IsNullOrEmpty(supplierMatch)) return supplierMatch;
                }

                return fallback;
            }

            return rootBrands.FirstOrDefault() ?? fallback;
        }

        private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string url, CancellationToken ct, string mediaType = "application/vnd.allegro.public.v1+json")
        {
            var token = await _auth.GetAccessTokenAsync(ct);

            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            if (!string.IsNullOrEmpty(mediaType))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
            }

            return request;
        }

        private async Task<T> DeserializeResponseAsync<T>(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Request failed: Status={Status}, Body={Body}", response.StatusCode, body);
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<T>(body, options);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to parse response JSON: {Body}", body);
                return default;
            }
        }
    }
}