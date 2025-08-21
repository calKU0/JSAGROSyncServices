using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
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

            var token = await _auth.GetAccessTokenAsync(ct);

            async Task<int> FetchCategory(string query, string queryType)
            {
                var url = $"/sale/matching-categories?name={Uri.EscapeDataString(query)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _http.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<MatchingCategoriesResponse>(json, options);

                    var category = result?.MatchingCategories?.FirstOrDefault();
                    if (category != null)
                    {
                        Log.Information($"Suggested Allegro category for product {code} by his {queryType}: {category.Name} ({category.Id})");
                        return Convert.ToInt32(category.Id);
                    }
                }
                else
                {
                    Log.Error($"Error querying categories by {queryType}: {response.StatusCode}, {await response.Content.ReadAsStringAsync()}");
                }

                return 0;
            }

            try
            {
                // 1 Try with product name first
                resultCategory = await FetchCategory(name, "name");

                // !!! DELETED OTHER MATCHING BECAUSE SOMETIMES IT RETURNED NOT RELEVANT CATEGORIES !!!

                // 2️ If no match, try with product code
                //if (resultCategory == 0 && !string.IsNullOrEmpty(code))
                //{
                //    resultCategory = await FetchCategory(code, "code");
                //}

                // 2️ If no match, try with product code
                //if (resultCategory == 0 && !string.IsNullOrEmpty(ean))
                //{
                //    resultCategory = await FetchCategory(ean, "ean");
                //}
            }
            catch (Exception ex)
            {
                Log.Error($"Exception in category suggestion: {ex}");
            }

            return resultCategory;
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