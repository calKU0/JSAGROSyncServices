using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.Settings;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace JSAGROAllegroSync.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(Product product, List<CompatibleProduct> compatibleList, List<AllegroCategory> allegroCategories, AppSettings appSettings)
        {
            return new ProductOfferRequest
            {
                Name = product.Name,
                ProductSet = BuildProductSet(product, appSettings),
                Category = new Category
                {
                    Id = product.DefaultAllegroCategory.ToString()
                },
                Stock = new Stock
                {
                    Available = Convert.ToInt32(Math.Floor(product.InStock)),
                    Unit = MapAllegroUnit(product.Unit)
                },
                SellingMode = new SellingMode
                {
                    Format = "BUY_NOW",
                    Price = new Price
                    {
                        Amount = CalculatePrice(product.PriceGross, product.DeliveryType, appSettings.AddPLNToBulkyProducts, appSettings.AddPLNToCustomProducts, appSettings.OwnMarginPercent, appSettings.AllegroMarginUnder5PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = product.Images.Select(i => i.AllegroUrl).ToList(),
                Description = BuildDescription(product),
                External = new External
                {
                    Id = product.CodeGaska
                },
                Publication = new Publication
                {
                    Status = "ACTIVE",
                    StartingAt = DateTime.UtcNow,
                },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates
                    {
                        Name = appSettings.AllegroDeliveryName
                    },
                    HandlingTime = product.DeliveryType == 0 ? appSettings.AllegroHandlingTime : appSettings.AllegroHandlingTimeCustomProducts,
                },
                Location = new Location
                {
                    City = "Bielsk Podlaski",
                    CountryCode = "PL",
                    PostCode = "17-100",
                    Province = "PODLASKIE"
                },
                Payments = new Payments
                {
                    Invoice = "VAT"
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = appSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = appSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = appSettings.AllegroImpliedWarranty }
                },
                Parameters = BuildParameters(product.Parameters, false),
                CompatibilityList = BuildCompatibilityList(product.DefaultAllegroCategory, product.Applications, allegroCategories, compatibleList)
            };
        }

        public static ProductOfferRequest PatchOffer(AllegroOffer offer, List<CompatibleProduct> compatibleList, List<AllegroCategory> allegroCategories, AppSettings appSettings)
        {
            return new ProductOfferRequest
            {
                Name = offer.Product.Name,
                Category = new Category
                {
                    Id = offer.Product.DefaultAllegroCategory.ToString() == "0" ? offer.CategoryId.ToString() : offer.Product.DefaultAllegroCategory.ToString(),
                },
                Stock = new Stock
                {
                    Available = Convert.ToInt32(Math.Floor(offer.Product.InStock)),
                    Unit = MapAllegroUnit(offer.Product.Unit)
                },
                SellingMode = new SellingMode
                {
                    Format = "BUY_NOW",
                    Price = new Price
                    {
                        Amount = CalculatePrice(offer.Product.PriceGross, offer.Product.DeliveryType, appSettings.AddPLNToBulkyProducts, appSettings.AddPLNToCustomProducts, appSettings.OwnMarginPercent, appSettings.AllegroMarginUnder5PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = offer.Product.Images.Select(i => i.AllegroUrl).ToList(),
                Description = BuildDescription(offer.Product),
                External = new External
                {
                    Id = offer.Product.CodeGaska
                },
                Publication = new Publication
                {
                    Status = offer.Product.InStock > 0 ? "ACTIVE" : "ENDED",
                    StartingAt = offer.StartingAt,
                },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates
                    {
                        Name = appSettings.AllegroDeliveryName
                    },
                    HandlingTime = offer.Product.DeliveryType == 0 ? appSettings.AllegroHandlingTime : appSettings.AllegroHandlingTimeCustomProducts
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = appSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = appSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = appSettings.AllegroImpliedWarranty }
                },
                Parameters = BuildParameters(offer.Product.Parameters, false),
                CompatibilityList = BuildCompatibilityList(offer.Product.DefaultAllegroCategory, offer.Product.Applications, allegroCategories, compatibleList)
            };
        }

        private static List<ProductSet> BuildProductSet(Product product, AppSettings appSettings)
        {
            var ProductSets = new List<ProductSet>();

            var Product = new ProductObject
            {
                Category = new Category { Id = product.DefaultAllegroCategory.ToString() },
                Images = product.Images.Select(i => i.AllegroUrl).ToList(),
                Parameters = BuildParameters(product.Parameters, true),
            };

            ProductSets.Add(new ProductSet
            {
                ProductObject = Product,
                Quantity = new Quantity
                {
                    Value = 1,
                },
                ResponsiblePerson = new ResponsiblePerson
                {
                    Name = appSettings.AllegroResponsiblePerson,
                },
                ResponsibleProducer = new ResponsibleProducer
                {
                    Type = "NAME",
                    Name = appSettings.AllegroResponsibleProducer,
                },
                SafetyInformation = new SafetyInformation
                {
                    Type = "TEXT",
                    Description = appSettings.AllegroSafetyMeasures
                },
            });

            return ProductSets;
        }

        private static string MapAllegroUnit(string productUnit)
        {
            if (string.IsNullOrWhiteSpace(productUnit))
                return "UNIT"; // default

            productUnit = productUnit.Trim().ToLower().Replace(".", "");

            if (productUnit == "szt")
                return "UNIT";
            else if (productUnit == "mb")
                return "METER";
            else if (productUnit == "para")
                return "PAIR";
            else if (productUnit == "kpl")
                return "SET";
            else
                return "UNIT"; // fallback for unknown units
        }

        private static List<Parameter> BuildParameters(ICollection<ProductParameter> parameters, bool isForProduct)
        {
            var result = new List<Parameter>();

            // parameters that should support multiple values
            var multiValueParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "numery katalogowe zamienników", "marka",
            };

            foreach (var param in parameters.Where(p => p.IsForProduct == isForProduct && p.CategoryParameter.Name != "EAN (GTIN)" && p.CategoryParameter.Name != "Informacje o bezpieczeństwie"))
            {
                List<string> values;

                if (!string.IsNullOrEmpty(param.Value) && multiValueParams.Contains(param.CategoryParameter.Name))
                {
                    values = param.Value
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    values = new List<string> { param.Value };
                }

                result.Add(new Parameter
                {
                    Name = param.CategoryParameter.Name,
                    Values = values
                });
            }

            return result;
        }

        public static CompatibilityList BuildCompatibilityList(int categoryId, IEnumerable<Application> applications, IEnumerable<AllegroCategory> categories, IEnumerable<CompatibleProduct> compatibleProducts)
        {
            if (applications == null || !applications.Any())
                return null;

            bool IsCategoryOrParent(int catId, int targetCategoryId)
            {
                var category = categories.FirstOrDefault(c => c.CategoryId == catId);
                while (category != null)
                {
                    if (category.CategoryId == targetCategoryId)
                        return true;

                    if (category.ParentId == null)
                        break;

                    category = categories.FirstOrDefault(c => c.Id == category.ParentId.Value);
                }
                return false;
            }

            var leafApps = applications
                .Where(a => !applications.Any(child => child.ParentID == a.ApplicationId))
                .OrderBy(a => a.ApplicationId)
                .ToList();

            List<Item> items = new List<Item>();

            if (IsCategoryOrParent(categoryId, 252204))
            {
                foreach (var leaf in leafApps)
                {
                    var compatItem = compatibleProducts
                        .FirstOrDefault(cp => cp.Name == leaf.Name && cp.Type == "ID");

                    if (compatItem != null)
                    {
                        string newValue = leaf.Name;
                        if (int.TryParse(leaf.Name, out int lastNodeNum))
                        {
                            lastNodeNum += 1;
                            string incremented = lastNodeNum.ToString();
                            if (compatibleProducts.Any(cp => cp.Id == incremented && cp.Type == "ID"))
                                newValue = incremented;
                        }

                        items.Add(new Item { Type = "ID", Text = compatItem.Id });
                    }
                }
            }
            else
            {
                var prohibitedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "marka"
                };

                foreach (var leaf in leafApps)
                {
                    var path = new List<string>();
                    var current = leaf;

                    // Traverse up to root
                    var fullPath = new List<Application>();
                    while (current != null)
                    {
                        fullPath.Insert(0, current); // root -> leaf
                        if (current.ParentID == 0) break;
                        current = applications.FirstOrDefault(a => a.ApplicationId == current.ParentID);
                    }

                    if (fullPath.Count == 0) continue;

                    // Always include root
                    path.Add(fullPath.First().Name);

                    // Decide whether to include parent of leaf
                    var leafName = fullPath.Last().Name;
                    bool leafIsNumber = int.TryParse(leafName, out _);
                    if (leafIsNumber && fullPath.Count > 2)
                    {
                        var parentOfLeaf = fullPath[fullPath.Count - 2];
                        if (parentOfLeaf.ParentID != fullPath.First().ApplicationId) // skip if 2nd level
                        {
                            path.Add(parentOfLeaf.Name);
                        }
                    }

                    // Always include leaf
                    path.Add(leafName);

                    string text = string.Join(" ", path);

                    if (prohibitedWords.Any(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    // Avoid duplicates
                    if (!items.Any(i => i.Text == text))
                    {
                        items.Add(new Item { Type = "TEXT", Text = text });
                    }
                }
            }

            if (!items.Any())
                return null;

            var cappedItems = items.Take(100).ToList();

            return new CompatibilityList { Items = cappedItems };
        }

        private static Description BuildDescription(Product product)
        {
            var description = new Description
            {
                Sections = new List<Section>()
            };

            var images = product.Images?.ToList() ?? new List<ProductImage>();
            int imageIndex = 0;

            // 0. First image full-width on top
            if (images.Any())
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "IMAGE",
                            Url = images[imageIndex++].AllegroUrl
                        }
                    }
                });
            }

            // 0. Product header (Name + Producer + Code)

            // Bold if contains Oryginał, Original, Org, oryginal, JAG
            //string HighlightKeywords(string input)
            //{
            //    if (string.IsNullOrEmpty(input)) return string.Empty;

            //    var keywords = new[] { "oryginał", "original", "org", "oryginal", "jag premium" };

            //    string result = System.Net.WebUtility.HtmlEncode(input);

            //    foreach (var keyword in keywords)
            //    {
            //        string pattern;
            //        if (keyword.Equals("jag premium", StringComparison.OrdinalIgnoreCase))
            //        {
            //            // Match JAG PREMIUM or JAG-PREMIUM
            //            pattern = @"\bjag[\s\-]+premium\b";
            //        }
            //        else
            //        {
            //            pattern = $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b";
            //        }

            //        var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            //        result = regex.Replace(result, "<b>$0</b>");
            //    }

            //    return result;
            //}

            string nameHtml = $"<h2>{System.Net.WebUtility.HtmlEncode(product.Name)}</h2>";
            string codeHtml = !string.IsNullOrWhiteSpace(product.CodeGaska)
                ? $"<p><b>Kod produktu: </b>{System.Net.WebUtility.HtmlEncode(product.CodeGaska)}</p>"
                : string.Empty;
            string producerHtml = !string.IsNullOrWhiteSpace(product.SupplierName)
                ? $"<p><b>Producent: </b>{System.Net.WebUtility.HtmlEncode(product.SupplierName)}</p>"
                : string.Empty;
            string descriptionHtml = !string.IsNullOrWhiteSpace(product.Description)
                ? $"<p><b>Opis: </b>{System.Net.WebUtility.HtmlEncode(product.Description)}</p>"
                : string.Empty;
            string technicalHtml = !string.IsNullOrWhiteSpace(product.TechnicalDetails)
                ? $"<p><b>Porady techniczne: </b>{System.Net.WebUtility.HtmlEncode(product.TechnicalDetails)}</p>"
                : string.Empty;
            string parametersHtml = product.Atributes != null && product.Atributes.Any()
                ? $"<p><b>Parametry/Wymiary: </b>{string.Join(", ", product.Atributes.Select(p => $"{System.Net.WebUtility.HtmlEncode(p.AttributeName)}: {System.Net.WebUtility.HtmlEncode(p.AttributeValue)}"))}</p>"
                : string.Empty;

            description.Sections.Add(new Section
            {
                SectionItems = new List<SectionItem>
                {
                    new SectionItem
                    {
                        Type = "TEXT",
                        Content = $"{nameHtml}{codeHtml}{producerHtml}{descriptionHtml}{technicalHtml}{parametersHtml}"
                    }
                }
            });

            // 2. Description
            //if (!string.IsNullOrWhiteSpace(product.Description))
            //{
            //    description.Sections.Add(new Section
            //    {
            //        SectionItems = new List<SectionItem>
            //        {
            //            new SectionItem
            //            {
            //                Type = "TEXT",
            //                Content = $"<p><b>Opis: </b>{System.Net.WebUtility.HtmlEncode(product.Description)}</p>"
            //            }
            //        }
            //    });
            //}

            // 3. Technical details
            //if (!string.IsNullOrWhiteSpace(product.TechnicalDetails))
            //{
            //    description.Sections.Add(new Section
            //    {
            //        SectionItems = new List<SectionItem>
            //        {
            //            new SectionItem
            //            {
            //                Type = "TEXT",
            //                Content = $"<p><b>Porady techniczne: </b>{System.Net.WebUtility.HtmlEncode(product.TechnicalDetails)}</p>"
            //            }
            //        }
            //    });
            //}

            // 4. Attributes/parameters
            //if (product.Atributes != null && product.Atributes.Any())
            //{
            //    var paramText = string.Join(", ", product.Atributes .Select(p => $"{System.Net.WebUtility.HtmlEncode(p.AttributeName)}: {System.Net.WebUtility.HtmlEncode(p.AttributeValue)}"));

            //    description.Sections.Add(new Section
            //    {
            //        SectionItems = new List<SectionItem>
            //        {
            //            new SectionItem
            //            {
            //                Type = "TEXT",
            //                Content = $"<p><b>Parametry/Wymiary: </b>{paramText}</p>"
            //            }
            //        }
            //    });
            //}

            // 5. Cross numbers
            if (product.CrossNumbers != null && product.CrossNumbers.Any())
            {
                var crossNumbersText = string.Join(", ", product.CrossNumbers.Select(c => System.Net.WebUtility.HtmlEncode(c.CrossNumberValue)));

                var sectionItems = new List<SectionItem>
                {
                    new SectionItem
                    {
                        Type = "TEXT",
                        Content = $"<p><b>Numery referencyjne: </b>{crossNumbersText}</p>"
                    }
                };

                if (imageIndex < images.Count)
                {
                    sectionItems.Add(new SectionItem
                    {
                        Type = "IMAGE",
                        Url = images[imageIndex++].AllegroUrl
                    });
                }

                description.Sections.Add(new Section { SectionItems = sectionItems });
            }

            // 6. Applications section
            if (product.Applications != null && product.Applications.Any())
            {
                // Build dictionary: parent path -> list of leaf names
                var applicationsByParent = product.Applications
                    .GroupBy(a => a.ParentID)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (applicationsByParent.ContainsKey(0))
                {
                    var rootApps = applicationsByParent[0];
                    var branches = new Dictionary<string, List<string>>(); // path -> leafs

                    void BuildBranch(Application app, string path)
                    {
                        string currentPath = string.IsNullOrEmpty(path)
                            ? app.Name
                            : path + " → " + app.Name;

                        if (applicationsByParent.ContainsKey(app.ApplicationId))
                        {
                            foreach (var child in applicationsByParent[app.ApplicationId])
                                BuildBranch(child, currentPath);
                        }
                        else
                        {
                            // Leaf node: add to dictionary
                            if (!branches.ContainsKey(path))
                                branches[path] = new List<string>();
                            branches[path].Add(app.Name);
                        }
                    }

                    foreach (var app in rootApps)
                        BuildBranch(app, string.Empty);

                    // Build display string
                    var branchTexts = new List<string>();
                    foreach (var kvp in branches)
                    {
                        string branchPath = kvp.Key; // parent path
                        string leafs = string.Join(", ", kvp.Value); // grouped leaf names
                        string fullText = string.IsNullOrEmpty(branchPath) ? leafs : branchPath + " → " + leafs;
                        branchTexts.Add(fullText);
                    }

                    string appsText = string.Join(", ", branchTexts.Select(System.Net.WebUtility.HtmlEncode));

                    var sectionItems = new List<SectionItem>();
                    if (imageIndex < images.Count)
                    {
                        sectionItems.Add(new SectionItem
                        {
                            Type = "IMAGE",
                            Url = images[imageIndex++].AllegroUrl
                        });
                    }

                    sectionItems.Add(new SectionItem
                    {
                        Type = "TEXT",
                        Content = $"<p><b>Zastosowanie: </b>{appsText}</p>"
                    });

                    description.Sections.Add(new Section { SectionItems = sectionItems });
                }
            }

            // 7. UWAGA section for packages (before remaining images)
            //var package = product.Packages?.FirstOrDefault(p => p.PackRequired == 1);
            //if (package != null)
            //{
            //    description.Sections.Add(new Section
            //    {
            //        SectionItems = new List<SectionItem>
            //        {
            //            new SectionItem
            //            {
            //                Type = "TEXT",
            //                Content = $"<p><b>UWAGA:</b> PODANA CENA KUP TERAZ TO CENA ZA 1 KOMPLET = {package.Qty} {System.Net.WebUtility.HtmlEncode(package.Unit)}</p>"
            //            }
            //        }
            //    });
            //}

            // 8. Remaining images at the bottom
            while (imageIndex < images.Count)
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "IMAGE",
                            Url = images[imageIndex++].AllegroUrl
                        }
                    }
                });
            }

            return description;
        }

        private static decimal CalculatePrice(decimal initialPrice, int productType, decimal addPLNToBulky, decimal addPLNToCustom, decimal ownMarginPercent, decimal marginLessThan5PLN, decimal marginMoreThan5PLNPercent, decimal marginMoreThan1000PLN)
        {
            var calculatedPrice = initialPrice;

            calculatedPrice = initialPrice * (1 + (ownMarginPercent / 100m));

            if (productType == 1) // bulky
            {
                calculatedPrice += addPLNToBulky;
            }
            else if (productType == 2) // custom
            {
                calculatedPrice += addPLNToCustom;
            }

            if (calculatedPrice < 5m)
            {
                calculatedPrice += marginLessThan5PLN;
            }
            else if (calculatedPrice >= 5m && calculatedPrice <= 1000m)
            {
                calculatedPrice *= (1 + marginMoreThan5PLNPercent / 100m);
            }
            else
            {
                calculatedPrice += marginMoreThan1000PLN;
            }

            return calculatedPrice;
        }
    }
}