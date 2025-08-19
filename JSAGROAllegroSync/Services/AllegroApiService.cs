using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace JSAGROAllegroSync.Services
{
    public class AllegroApiService
    {
        private readonly AllegroAuthService _auth;
        private readonly HttpClient _http;

        public AllegroApiService(AllegroAuthService auth, HttpClient httpClient = null)
        {
            _auth = auth;
            _http = httpClient ?? new HttpClient();
        }

        public async Task SyncProducts(CancellationToken ct = default)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // TODO: wywołania REST Allegro
        }

        public async Task SyncProductDetails(CancellationToken ct = default)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // TODO: wywołania REST Allegro - szczegóły
        }

        public async Task<string> GenerateXMLFile()
        {
            string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string resultFileName = $"products_{timestamp}.xml";
            string resultFilePath = Path.Combine(resultPath, resultFileName);
            List<Product> products = new List<Product>();

            // Clean up old files
            CleanupOldXmlFiles();

            try
            {
                if (!Directory.Exists(resultPath))
                {
                    Directory.CreateDirectory(resultPath);
                }

                using (var db = new MyDbContext())
                {
                    products = await db.Products
                        .Include(p => p.Categories)
                        .Include(p => p.Applications)
                        .Include(p => p.Parameters)
                        .Include(p => p.CrossNumbers)
                        .Include(p => p.Packages)
                        .Include(p => p.RecommendedParts)
                        .Include(p => p.Components)
                        .Include(p => p.Files)
                        .Include(p => p.Images)
                        .Where(p => p.Categories.Any() && p.Archived == false)
                        .ToListAsync();
                }

                if (products.Any())
                {
                    XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 };

                    using (XmlWriter writer = XmlWriter.Create(resultFilePath, settings))
                    {
                        writer.WriteStartElement("Products");
                        foreach (var product in products)
                        {
                            writer.WriteStartElement("Product");

                            WriteRawElement(writer, "Id", product.Id.ToString());
                            WriteRawElement(writer, "CodeGaska", product.CodeGaska);
                            WriteRawElement(writer, "Name", product.Name);
                            if (!string.IsNullOrEmpty(product.SupplierName))
                                WriteRawElement(writer, "Supplier", product.SupplierName);

                            WriteRawElement(writer, "EAN", product.Ean);
                            WriteRawElement(writer, "WeightNet", product.WeightNet.ToString());
                            WriteRawElement(writer, "WeightGross", product.WeightGross.ToString());
                            WriteRawElement(writer, "Stock", product.InStock.ToString());
                            WriteRawElement(writer, "PriceNet", product.PriceNet.ToString());
                            WriteRawElement(writer, "PriceGross", product.PriceGross.ToString());

                            // Prepare the HTML description
                            var descriptionBuilder = new StringBuilder();
                            if (!string.IsNullOrEmpty(product.Description))
                            {
                                descriptionBuilder.Append("<p><b>Opis: </b>");
                                descriptionBuilder.Append(product.Description);
                                descriptionBuilder.Append("</p>");
                            }

                            if (!string.IsNullOrEmpty(product.TechnicalDetails))
                            {
                                descriptionBuilder.Append("<p><b>Porady techniczne: </b>");
                                descriptionBuilder.Append(product.TechnicalDetails);
                                descriptionBuilder.Append("</p>");
                            }

                            if (product.Parameters != null && product.Parameters.Any())
                            {
                                descriptionBuilder.Append("<p><b>Parametry: </b>");
                                descriptionBuilder.Append(string.Join(", ", product.Parameters?.Select(param => $"{param.AttributeName} : {param.AttributeValue}") ?? new List<string>()));
                                descriptionBuilder.Append("</p>");
                            }

                            if (product.CrossNumbers != null && product.CrossNumbers.Any())
                            {
                                descriptionBuilder.Append("<p><b>Numery referencyjne: </b>");
                                descriptionBuilder.Append(string.Join(", ", product.CrossNumbers.Select(c => c.CrossNumberValue)));
                                descriptionBuilder.Append("</p>");
                            }

                            if (product.Applications != null && product.Applications.Any())
                            {
                                // Group applications by ParentID to build a tree
                                var applicationsByParent = product.Applications
                                    .GroupBy(a => a.ParentID)
                                    .ToDictionary(g => g.Key, g => g.ToList());

                                string BuildApplicationHtmlList(List<Application> apps, int depth = 1)
                                {
                                    if (apps == null || !apps.Any())
                                        return string.Empty;

                                    var sb = new StringBuilder();
                                    sb.Append("<ul>");

                                    if (depth >= 3)
                                    {
                                        var nodesWithChildren = apps.Where(a => applicationsByParent.ContainsKey(a.ApplicationId)).ToList();
                                        var leafNodes = apps.Where(a => !applicationsByParent.ContainsKey(a.ApplicationId)).ToList();

                                        // Check if all siblings are leaves (no siblings with children)
                                        bool allSiblingsAreLeaves = !apps.Any(a => applicationsByParent.ContainsKey(a.ApplicationId));

                                        // Render nodes with children first
                                        foreach (var app in nodesWithChildren)
                                        {
                                            sb.Append("<li>");
                                            sb.Append(app.Name);
                                            sb.Append(BuildApplicationHtmlList(applicationsByParent[app.ApplicationId], depth + 1));
                                            sb.Append("</li>");
                                        }

                                        if (leafNodes.Any())
                                        {
                                            if (allSiblingsAreLeaves)
                                            {
                                                // All siblings are leaves — group them
                                                sb.Append("<li>");
                                                sb.Append(string.Join(", ", leafNodes.Select(a => a.Name)));
                                                sb.Append("</li>");
                                            }
                                            else
                                            {
                                                // Mixed siblings — output each leaf separately
                                                foreach (var app in leafNodes)
                                                {
                                                    sb.Append("<li>");
                                                    sb.Append(app.Name);
                                                    sb.Append("</li>");
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // depth 1 or 2: no grouping - all separate <li>
                                        foreach (var app in apps)
                                        {
                                            sb.Append("<li>");
                                            sb.Append(app.Name);

                                            if (applicationsByParent.ContainsKey(app.ApplicationId))
                                                sb.Append(BuildApplicationHtmlList(applicationsByParent[app.ApplicationId], depth + 1));

                                            sb.Append("</li>");
                                        }
                                    }

                                    sb.Append("</ul>");
                                    return sb.ToString();
                                }

                                if (applicationsByParent.ContainsKey(0))
                                {
                                    var rootApplications = applicationsByParent[0];

                                    descriptionBuilder.Append("<div class=\"product-applications\">");
                                    descriptionBuilder.Append("<p><b>Zastosowanie: </b></p>");
                                    descriptionBuilder.Append(BuildApplicationHtmlList(rootApplications));
                                    descriptionBuilder.Append("</div>");
                                }
                            }

                            WriteRawElement(writer, "Description", descriptionBuilder.ToString());

                            // Images
                            if (product.Images != null && product.Images.Any())
                            {
                                writer.WriteStartElement("Images");
                                foreach (var img in product.Images)
                                {
                                    writer.WriteStartElement("Image");
                                    WriteRawElement(writer, "ImageTitle", img.Title);
                                    WriteRawElement(writer, "ImageUrl", img.Url);
                                    writer.WriteEndElement(); // Image
                                }
                                writer.WriteEndElement(); // Images
                            }

                            // Packages
                            if (product.Packages != null && product.Packages.Any())
                            {
                                writer.WriteStartElement("Packages");
                                foreach (var pack in product.Packages)
                                {
                                    writer.WriteStartElement("Package");
                                    WriteRawElement(writer, "PackUnit", pack.PackUnit);
                                    WriteRawElement(writer, "PackQty", pack.PackQty.ToString());
                                    WriteRawElement(writer, "PackNettWeight", pack.PackNettWeight.ToString());
                                    WriteRawElement(writer, "PackGrossWeight", pack.PackGrossWeight.ToString());
                                    WriteRawElement(writer, "PackEan", pack.PackEan);
                                    WriteRawElement(writer, "PackRequired", pack.PackRequired.ToString());
                                    writer.WriteEndElement(); // Package
                                }
                                writer.WriteEndElement(); // Packages
                            }

                            // Components
                            if (product.Components != null && product.Components.Any())
                            {
                                writer.WriteStartElement("Components");
                                foreach (var comp in product.Components)
                                {
                                    writer.WriteStartElement("Component");
                                    WriteRawElement(writer, "TwrID", comp.TwrID.ToString());
                                    WriteRawElement(writer, "CodeGaska", comp.CodeGaska);
                                    WriteRawElement(writer, "Qty", comp.Qty.ToString());
                                    writer.WriteEndElement(); // Component
                                }
                                writer.WriteEndElement(); // Components
                            }

                            // Parameters
                            if (product.Parameters != null && product.Parameters.Any())
                            {
                                writer.WriteStartElement("Parameters");
                                foreach (var param in product.Parameters)
                                {
                                    writer.WriteStartElement("Parameter");
                                    WriteRawElement(writer, "AttributeName", param.AttributeName);
                                    WriteRawElement(writer, "AttributeValue", param.AttributeValue);
                                    writer.WriteEndElement(); // Parameter
                                }
                                writer.WriteEndElement(); // Parameters
                            }

                            // RecommendedParts
                            if (product.RecommendedParts != null && product.RecommendedParts.Any())
                            {
                                writer.WriteStartElement("RecommendedParts");
                                foreach (var rec in product.RecommendedParts)
                                {
                                    writer.WriteStartElement("RecommendedPart");
                                    WriteRawElement(writer, "TwrId", rec.TwrID.ToString());
                                    WriteRawElement(writer, "CodeGaska", rec.CodeGaska);
                                    WriteRawElement(writer, "Qty", rec.Qty.ToString());
                                    writer.WriteEndElement(); // RecommendedPart
                                }
                                writer.WriteEndElement(); // RecommendedParts
                            }

                            writer.WriteEndElement(); // Close Product
                        }
                        writer.WriteEndElement(); // Close Products
                    }

                    Log.Information($"Generated XML file in {resultFilePath}");
                }
                else
                {
                    Log.Warning("Database is empty. No products to send.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while making XML file."); ;
            }

            return resultFilePath;
        }

        public async Task UploadFileToFtp(string localFilePath)
        {
            try
            {
                string ftpUri = $"ftp://{_allegroSettings.Ip}:{_allegroSettings.Port}/products.xml";
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(ftpUri);
                request.Method = WebRequestMethods.Ftp.UploadFile;
                request.Credentials = new NetworkCredential(_allegroSettings.Username, _allegroSettings.Password);

                byte[] fileContents = File.ReadAllBytes(localFilePath);
                request.ContentLength = fileContents.Length;

                using (Stream requestStream = await request.GetRequestStreamAsync())
                {
                    await requestStream.WriteAsync(fileContents, 0, fileContents.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error uploading file to FTP.");
            }
        }

        private static void WriteRawElement(XmlWriter writer, string elementName, string value)
        {
            string sanitizedValue = CleanAndSanitizeText(value);
            writer.WriteStartElement(elementName);
            writer.WriteRaw($"<![CDATA[{sanitizedValue}]]>");
            writer.WriteEndElement();
        }

        private static string CleanAndSanitizeText(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            // First, normalize and replace known invisible/unwanted characters
            var normalized = input.Normalize(NormalizationForm.FormC);

            normalized = normalized
                .Replace('\u00A0', ' ')  // non-breaking space
                .Replace('\u0081', ' ')  // likely your corrupted character
                .Replace('\u200B', ' ')  // zero-width space
                .Replace('\u202F', ' ')  // narrow no-break space
                .Replace('\uFEFF', ' ')  // byte-order mark
                .Replace('\u00AD', ' ')  // soft hyphen
                .Trim();

            // Then remove XML-invalid characters
            var cleaned = new string(normalized.Where(c =>
                c == '\t' || c == '\n' || c == '\r' ||
                (c >= 32 && c <= 0xD7FF) ||
                (c >= 0xE000 && c <= 0xFFFD) ||
                (c >= 0x10000 && c <= 0x10FFFF)
            ).ToArray());

            return cleaned;
        }

        private void CleanupOldXmlFiles()
        {
            try
            {
                string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result");

                if (!Directory.Exists(resultPath))
                    return;

                var files = Directory.GetFiles(resultPath, "*.xml");

                foreach (var file in files)
                {
                    var creationTime = File.GetCreationTime(file);
                    if ((DateTime.Now - creationTime).TotalDays > 1)
                    {
                        File.Delete(file);
                        Log.Information($"Deleted old XML file: {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during cleanup of old XML files.");
            }
        }
    }
}