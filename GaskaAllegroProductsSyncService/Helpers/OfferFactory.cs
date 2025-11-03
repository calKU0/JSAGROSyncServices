using AllegroGaskaProductsSyncService.DTOs.AllegroApi;
using AllegroGaskaProductsSyncService.Models;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Settings;
using System.Globalization;
using System.Text;

namespace AllegroGaskaProductsSyncService.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(Product product, List<AllegroCategory> allegroCategories, AppSettings appSettings)
        {
            int productQuantity = product.Packages.Any(p => p.PackRequired == 1) ? Convert.ToInt32(product.Packages.Where(p => p.PackRequired == 1).Select(p => p.PackQty).FirstOrDefault()) : 1;
            return new ProductOfferRequest
            {
                Name = product.Name,
                ProductSet = BuildProductSet(product, productQuantity, appSettings),
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
                        Amount = CalculatePrice(product.PriceGross, product.DeliveryType, productQuantity, appSettings.AddPLNToBulkyProducts, appSettings.AddPLNToCustomProducts, appSettings.OwnMarginPercent, appSettings.AllegroMarginUnder5PLN, appSettings.OwnMarginPercentUnder10PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = GetOfferImages(product),
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
                CompatibilityList = product.BuildCompatibilitySet ? BuildCompatibilityList(product.DefaultAllegroCategory, product.Applications, allegroCategories) : null
            };
        }

        public static ProductOfferRequest PatchOffer(AllegroOffer offer, List<AllegroCategory> allegroCategories, AppSettings appSettings)
        {
            int productQuantity = offer.Product.Packages.Any(p => p.PackRequired == 1) ? Convert.ToInt32(offer.Product.Packages.Where(p => p.PackRequired == 1).Select(p => p.PackQty).FirstOrDefault()) : 1;
            return new ProductOfferRequest
            {
                //Name = offer.Product.Name,
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
                        Amount = CalculatePrice(offer.Product.PriceGross, offer.Product.DeliveryType, productQuantity, appSettings.AddPLNToBulkyProducts, appSettings.AddPLNToCustomProducts, appSettings.OwnMarginPercent, appSettings.OwnMarginPercentUnder10PLN, appSettings.AllegroMarginUnder5PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = GetOfferImages(offer.Product),
                Description = BuildDescription(offer.Product),
                External = new External
                {
                    Id = offer.Product.CodeGaska
                },
                Publication = new Publication
                {
                    Status = offer.Product.InStock >= appSettings.MinProductStock ? "ACTIVE" : "ENDED",
                    StartingAt = null,
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
                CompatibilityList = offer.Product.BuildCompatibilitySet ? BuildCompatibilityList(offer.Product.DefaultAllegroCategory, offer.Product.Applications, allegroCategories) : null
            };
        }

        private static List<string> GetOfferImages(Product product)
        {
            List<string> images = product.Images
                .Where(i => !string.IsNullOrEmpty(i.AllegroUrl))
                .Select(i => i.AllegroUrl)
                .ToList();

            string logoUrl = product.Images
                .Where(i => !string.IsNullOrEmpty(i.AllegroLogoUrl))
                .Select(i => i.AllegroLogoUrl)
                .FirstOrDefault();

            if (!string.IsNullOrEmpty(logoUrl))
            {
                images.Add(logoUrl);
            }

            return images;
        }

        private static List<ProductSet> BuildProductSet(Product product, int quantity, AppSettings appSettings, string fallbackCat = "319123")
        {
            var ProductSets = new List<ProductSet>();

            var Product = new ProductObject
            {
                Name = product.Name,
                Category = new Category { Id = product.DefaultAllegroCategory.ToString() == "0" ? fallbackCat : product.DefaultAllegroCategory.ToString() },
                Images = product.Images.Select(i => i.AllegroUrl).ToList(),
                Parameters = BuildParameters(product.Parameters, true),
            };

            ProductSets.Add(new ProductSet
            {
                ProductObject = Product,
                Quantity = new Quantity
                {
                    Value = quantity,
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
                if (string.IsNullOrWhiteSpace(param.Value))
                    continue;

                // 1. Remove all control characters (ASCII < 0x20 or 0x7F–0x9F) except space
                var cleaned = new string(param.Value
                    .Where(ch => !char.IsControl(ch) || ch == ' ')
                    .ToArray())
                    .Trim();

                List<string> values;

                if (multiValueParams.Contains(param.CategoryParameter.Name))
                {
                    // 2. Split by comma OR whitespace
                    values = cleaned
                        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // 3. Apply max=9 for parameter id 215941
                    if (param.CategoryParameter.Name == "Numery katalogowe zamienników")
                    {
                        values = values.Take(15).ToList();
                    }
                }
                else
                {
                    values = new List<string> { cleaned };
                }

                if (values.Count > 0)
                {
                    result.Add(new Parameter
                    {
                        Name = param.CategoryParameter.Name,
                        Values = values
                    });
                }
            }

            return result;
        }

        public static CompatibilityList BuildCompatibilityList(int categoryId, IEnumerable<Application> applications, IEnumerable<AllegroCategory> categories)
        {
            if (applications == null || !applications.Any())
                return null;

            var categoryExists = categories.Any(c => c.Id == categoryId || c.CategoryId == categoryId.ToString());
            if (!categoryExists)
                return null;

            bool IsCategoryOrParent(int catId, string targetCategoryId)
            {
                var category = categories.FirstOrDefault(c => c.CategoryId == catId.ToString() || c.Id == catId);
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

            if (IsCategoryOrParent(categoryId, "252204"))
            {
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

            var cappedItems = items.Take(99).ToList();

            return new CompatibilityList { Items = cappedItems };
        }

        private static Description BuildDescription(Product product)
        {
            var description = new Description
            {
                Sections = new List<Section>()
            };

            var images = GetOfferImages(product);
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
                            Url = images[imageIndex++]
                        }
                    }
                });
            }

            // 0. Product header (Name + Producer + Code)

            string nameHtml = $"<p><b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Name))}</b></p>";
            string codeHtml = !string.IsNullOrWhiteSpace(product.CodeGaska)
                ? $"<p><b>Kod produktu: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.CodeGaska))}</p>"
                : string.Empty;
            string producerHtml = !string.IsNullOrWhiteSpace(product.SupplierName)
                ? $"<p><b>Producent: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.SupplierName))}</p>"
                : string.Empty;
            string descriptionHtml = !string.IsNullOrWhiteSpace(product.Description)
                ? $"<p><b>Opis: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Description))}</p>"
                : string.Empty;
            string technicalHtml = !string.IsNullOrWhiteSpace(product.TechnicalDetails)
                ? $"<p><b>Porady techniczne: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.TechnicalDetails))}</p>"
                : string.Empty;

            string parametersHtml = string.Empty;
            if (product.Atributes != null && product.Atributes.Any())
            {
                var attributesList = string.Join("", product.Atributes.Select(p =>
                    $"<li>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.AttributeName))}: {RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.AttributeValue))}</li>"
                ));
                parametersHtml = $"<p><b>Parametry/Wymiary:</b></p><ul>{attributesList}</ul>";
            }

            var package = product.Packages?.FirstOrDefault(p => p.PackRequired == 1);
            string warning = string.Empty;

            if (string.Equals(product.Unit, "MB", StringComparison.OrdinalIgnoreCase))
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode($"PODANA CENA KUP TERAZ TO CENA ZA 1 METR BIEŻĄCY")}</p>";
            }

            if (package != null)
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode($"PODANA CENA KUP TERAZ TO CENA ZA 1 KOMPLET = {package.PackQty} {ConjugationHelper.Unit(Convert.ToInt32(package.PackQty), product.Unit).ToUpper()}")}</p>";
            }

            string crossNumbersText = string.Empty;
            if (product.CrossNumbers != null && product.CrossNumbers.Any())
            {
                var crossNumbers = string.Join(", ", product.CrossNumbers.Select(c => System.Net.WebUtility.HtmlEncode(c.CrossNumberValue)));
                crossNumbersText = $"<p><b>Numery referencyjne: </b>{crossNumbers}</p>";
            }

            // Build the content string for text fields
            var contentBuilder = new StringBuilder();
            contentBuilder.Append(nameHtml)
                          .Append(codeHtml)
                          .Append(producerHtml)
                          .Append(descriptionHtml)
                          .Append(technicalHtml)
                          .Append(parametersHtml)
                          .Append(crossNumbersText)
                          .Append(warning);

            // Build the section
            var sectionItems = new List<SectionItem>
            {
                new SectionItem
                {
                    Type = "TEXT",
                    Content = contentBuilder.ToString()
                }
            };

            // Add image
            if (imageIndex < images.Count - 1)
            {
                sectionItems.Add(new SectionItem
                {
                    Type = "IMAGE",
                    Url = images[imageIndex++]
                });
            }

            description.Sections.Add(new Section
            {
                SectionItems = sectionItems
            });

            // 6. Applications section
            if (product.Applications != null && product.Applications.Any())
            {
                var applicationsByParent = product.Applications
                    .GroupBy(a => a.ParentID)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (applicationsByParent.ContainsKey(0))
                {
                    var rootApps = applicationsByParent[0];

                    string GetLeafNames(int parentId)
                    {
                        if (!applicationsByParent.ContainsKey(parentId))
                            return string.Empty;

                        var leafNames = new List<string>();

                        foreach (var child in applicationsByParent[parentId])
                        {
                            if (applicationsByParent.ContainsKey(child.ApplicationId))
                            {
                                // Recurse into grandchildren
                                leafNames.AddRange(GetLeafNames(child.ApplicationId).Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries));
                            }
                            else
                            {
                                leafNames.Add(RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(child.Name)));
                            }
                        }

                        return string.Join(", ", leafNames);
                    }

                    var listItems = new List<string>();

                    foreach (var rootApp in rootApps)
                    {
                        if (!applicationsByParent.ContainsKey(rootApp.ApplicationId)) continue;
                        foreach (var secondLevel in applicationsByParent[rootApp.ApplicationId])
                        {
                            string leafs = GetLeafNames(secondLevel.ApplicationId);
                            string li = $"<li><b>{System.Net.WebUtility.HtmlEncode(rootApp.Name)} - {System.Net.WebUtility.HtmlEncode(secondLevel.Name)}</b>: {leafs}</li>";
                            listItems.Add(li);
                        }
                    }

                    string appsText = $"<ul>{string.Join("", listItems)}</ul>";

                    var appSectionItems = new List<SectionItem>();
                    if (imageIndex < images.Count - 1)
                    {
                        appSectionItems.Add(new SectionItem
                        {
                            Type = "IMAGE",
                            Url = images[imageIndex++]
                        });
                    }

                    appSectionItems.Add(new SectionItem
                    {
                        Type = "TEXT",
                        Content = $"<p><b>Zastosowanie: </b></p>{appsText}"
                    });

                    description.Sections.Add(new Section { SectionItems = appSectionItems });
                }
            }

            while (imageIndex < images.Count)
            {
                var sectionImageItems = new List<SectionItem>();

                // First image
                sectionImageItems.Add(new SectionItem
                {
                    Type = "IMAGE",
                    Url = images[imageIndex++]
                });

                // Add second image if available
                if (imageIndex < images.Count)
                {
                    sectionImageItems.Add(new SectionItem
                    {
                        Type = "IMAGE",
                        Url = images[imageIndex++]
                    });
                }

                description.Sections.Add(new Section
                {
                    SectionItems = sectionImageItems
                });
            }

            return description;
        }

        private static decimal CalculatePrice(
            decimal initialPrice,
            int productType,
            int quantity,
            decimal addPLNToBulky,
            decimal addPLNToCustom,
            decimal ownMarginPercent,
            decimal ownMarginPercentLessThan10PLN,
            decimal marginLessThan5PLN,
            decimal marginMoreThan5PLNPercent,
            decimal marginMoreThan1000PLN)
        {
            var calculatedPrice = initialPrice;

            // Apply own margin
            decimal effectiveMargin = ownMarginPercent;

            if (initialPrice < 10m)
                effectiveMargin = ownMarginPercentLessThan10PLN;

            // Apply own margin
            calculatedPrice = initialPrice * quantity * (1 + (effectiveMargin / 100m));

            // Product type adjustments
            if (productType == 1) // bulky
            {
                calculatedPrice += addPLNToBulky;
            }
            else if (productType == 2) // custom
            {
                calculatedPrice += addPLNToCustom;
            }

            // Tiered pricing rules
            if (calculatedPrice < 5m)
            {
                var withSmallMargin = calculatedPrice + marginLessThan5PLN;

                if (withSmallMargin < 5m)
                {
                    calculatedPrice = withSmallMargin;
                    return calculatedPrice;
                }
                else
                {
                    return calculatedPrice * (1 + marginMoreThan5PLNPercent / 100m);
                }
            }

            if (calculatedPrice >= 5m && calculatedPrice <= 1000m)
            {
                var tempPrice = calculatedPrice * (1 + marginMoreThan5PLNPercent / 100m);

                if (tempPrice > 1000m)
                {
                    // ignore percent margin, apply 1000+ rule
                    calculatedPrice += marginMoreThan1000PLN;
                    return calculatedPrice;
                }

                calculatedPrice = tempPrice;
                return calculatedPrice;
            }

            // Over 1000 case
            calculatedPrice += marginMoreThan1000PLN;
            return calculatedPrice;
        }

        private static string RemoveHiddenAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Remove ASCII control characters except newline (10) and carriage return (13)
            return new string(input.Where(c => c >= 32 || c == 10 || c == 13).ToArray());
        }
    }
}