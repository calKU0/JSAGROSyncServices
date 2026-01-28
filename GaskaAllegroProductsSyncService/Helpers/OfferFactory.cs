using GaskaAllegroProductsSyncService.Models;
using GaskaAllegroProductsSyncService.Models.Product;
using GaskaAllegroProductsSyncService.Settings;
using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Helpers;
using JSAGROSyncServices.Shared.Models;
using System.Globalization;
using System.Text;

namespace GaskaAllegroProductsSyncService.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(
            Product product,
            List<AllegroCategory> allegroCategories,
            AppSettings appSettings,
            AllegroSettings allegroSettings,
            PriceSettings priceSettings)
        {
            var quantity = GetPackageQuantity(product, appSettings.BundleProductsUnderPriceNet);

            return CreateOffer(
                product,
                quantity,
                allegroCategories,
                allegroSettings,
                priceSettings,
                publicationStatus: "ACTIVE",
                startingAt: DateTime.UtcNow,
                categoryId: product.DefaultAllegroCategory.ToString(),
                name: product.Name,
                stockOverride: null,
                includeCategory: true);
        }

        public static ProductOfferRequest PatchOffer(
            AllegroOffer offer,
            List<AllegroCategory> allegroCategories,
            AppSettings appSettings,
            AllegroSettings allegroSettings,
            PriceSettings priceSettings)
        {
            var product = offer.Product;
            var quantity = GetPackageQuantity(product, appSettings.BundleProductsUnderPriceNet);

            return CreateOffer(
                product,
                quantity,
                allegroCategories,
                allegroSettings,
                priceSettings,
                publicationStatus: product.InStock >= appSettings.MinProductStock && product.PriceNet >= appSettings.MinProductPriceNet ? "ACTIVE" : "ENDED",
                startingAt: null,
                categoryId: null,          // nie nadpisujemy kategorii przy patchu
                name: null,                // nie nadpisujemy nazwy przy patchu
                stockOverride: Convert.ToInt32(Math.Floor(product.InStock)),
                includeCategory: false);
        }

        private static ProductOfferRequest CreateOffer(
            Product product,
            int quantity,
            List<AllegroCategory> allegroCategories,
            AllegroSettings allegroSettings,
            PriceSettings priceSettings,
            string publicationStatus,
            DateTime? startingAt,
            string? categoryId,
            string? name,
            int? stockOverride,
            bool includeCategory)
        {
            var price = CalculatePrice(
                product.PriceGross,
                product.PriceNet,
                priceSettings.MinProductPriceNetForFreeDelivery,
                priceSettings.StandardDeliveryPriceNet,
                priceSettings.BulkyDeliveryPriceNet,
                priceSettings.CustomDeliveryPriceNet,
                priceSettings.DropshippingPriceNet,
                product.DeliveryType,
                quantity,
                priceSettings.OwnMarginPercent,
                priceSettings.AllegroMarginUnder5PLN,
                priceSettings.AllegroMarginBetween5and1000PLNPercent,
                priceSettings.AllegroMarginMoreThan1000PLN);

            var offer = new ProductOfferRequest
            {
                ProductSet = BuildProductSet(product, quantity, allegroSettings),
                Stock = new Stock
                {
                    Available = stockOverride ?? Convert.ToInt32(Math.Floor(product.InStock)),
                    Unit = MapAllegroUnit(product.Unit)
                },
                SellingMode = new SellingMode
                {
                    Format = "BUY_NOW",
                    Price = new Price
                    {
                        Amount = price.ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = GetOfferImages(product),
                Description = BuildDescription(product),
                External = new External { Id = product.CodeGaska },
                Publication = new Publication { Status = publicationStatus, StartingAt = startingAt },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates { Name = allegroSettings.AllegroDeliveryName },
                    HandlingTime = product.DeliveryType == 0
                        ? allegroSettings.AllegroHandlingTime
                        : allegroSettings.AllegroHandlingTimeCustomProducts
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = allegroSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = allegroSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = allegroSettings.AllegroImpliedWarranty }
                },
                Parameters = BuildParameters(product.Parameters, isForProduct: false),
                CompatibilityList = product.BuildCompatibilitySet
                    ? BuildCompatibilityList(product.DefaultAllegroCategory, product.Applications, allegroCategories)
                    : null
            };

            if (includeCategory && categoryId is not null)
            {
                offer.Category = new Category { Id = categoryId };
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                offer.Name = name;
            }

            return offer;
        }

        private static int GetPackageQuantity(Product product, decimal minBundleNetValue)
        {
            var baseQty = product.Packages.Any(p => p.PackRequired == 1)
                ? Convert.ToInt32(product.Packages.First(p => p.PackRequired == 1).PackQty)
                : 1;

            if (baseQty < 1)
                baseQty = 1;

            if (product.PriceNet <= 0m || product.PriceNet >= 100m)
                return baseQty;

            var netValueForBase = product.PriceNet * baseQty;
            if (netValueForBase >= minBundleNetValue)
                return baseQty;

            var multiplier = (int)Math.Ceiling(minBundleNetValue / netValueForBase);
            return baseQty * multiplier;
        }

        private static List<string> GetOfferImages(Product product)
        {
            var images = product.Images
                .Select(i => i.AllegroUrl)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();

            var logoUrl = product.Images
                .Select(i => i.AllegroLogoUrl)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));

            if (!string.IsNullOrWhiteSpace(logoUrl))
            {
                images.Add(logoUrl);
            }

            return images;
        }

        private static List<ProductSet> BuildProductSet(
            Product product,
            int quantity,
            AllegroSettings allegroSettings,
            string fallbackCat = "319123")
        {
            var categoryId = product.DefaultAllegroCategory.ToString();
            var productObject = new ProductObject
            {
                Name = product.Name,
                Category = new Category { Id = categoryId == "0" ? fallbackCat : categoryId },
                Images = product.Images.Select(i => i.AllegroUrl).ToList(),
                Parameters = BuildParameters(product.Parameters, isForProduct: true),
            };

            return new List<ProductSet>
            {
                new()
                {
                    ProductObject = productObject,
                    Quantity = new Quantity { Value = quantity },
                    ResponsiblePerson = new ResponsiblePerson { Name = allegroSettings.AllegroResponsiblePerson },
                    ResponsibleProducer = new ResponsibleProducer { Type = "NAME", Name = allegroSettings.AllegroResponsibleProducer },
                    SafetyInformation = new SafetyInformation { Type = "TEXT", Description = allegroSettings.AllegroSafetyMeasures },
                }
            };
        }

        private static string MapAllegroUnit(string productUnit)
        {
            if (string.IsNullOrWhiteSpace(productUnit))
                return "UNIT";

            return productUnit.Trim().ToLowerInvariant().Replace(".", "") switch
            {
                "szt" => "UNIT",
                "para" => "PAIR",
                "kpl" => "SET",
                _ => "UNIT"
            };
        }

        private static List<Parameter> BuildParameters(ICollection<ProductParameter> parameters, bool isForProduct)
        {
            var result = new List<Parameter>();

            var multiValueParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "numery katalogowe zamienników",
                "marka",
            };

            foreach (var param in parameters.Where(p =>
                         p.IsForProduct == isForProduct &&
                         p.CategoryParameter.Name != "EAN (GTIN)" &&
                         p.CategoryParameter.Name != "Informacje o bezpieczeństwie"))
            {
                if (string.IsNullOrWhiteSpace(param.Value))
                    continue;

                var cleaned = new string(param.Value
                    .Where(ch => !char.IsControl(ch) || ch == ' ')
                    .ToArray())
                    .Trim();

                List<string> values;
                if (multiValueParams.Contains(param.CategoryParameter.Name))
                {
                    values = cleaned
                        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(15)
                        .ToList();
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

        public static CompatibilityList? BuildCompatibilityList(
            int categoryId,
            IEnumerable<Application> applications,
            IEnumerable<AllegroCategory> categories)
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

            var items = new List<Item>();

            if (!IsCategoryOrParent(categoryId, "252204"))
            {
                var prohibitedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "marka" };

                foreach (var leaf in leafApps)
                {
                    var fullPath = new List<Application>();
                    var current = leaf;
                    while (current != null)
                    {
                        fullPath.Insert(0, current);
                        if (current.ParentID == 0) break;
                        current = applications.FirstOrDefault(a => a.ApplicationId == current.ParentID);
                    }

                    if (fullPath.Count == 0)
                        continue;

                    var path = new List<string> { fullPath.First().Name };
                    var leafName = fullPath.Last().Name;
                    var leafIsNumber = int.TryParse(leafName, out _);

                    if (leafIsNumber && fullPath.Count > 2)
                    {
                        var parentOfLeaf = fullPath[^2];
                        if (parentOfLeaf.ParentID != fullPath.First().ApplicationId)
                        {
                            path.Add(parentOfLeaf.Name);
                        }
                    }

                    path.Add(leafName);

                    var text = string.Join(" ", path);
                    if (prohibitedWords.Any(word => text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
                        continue;

                    if (!items.Any(i => i.Text == text))
                    {
                        items.Add(new Item { Type = "TEXT", Text = text });
                    }
                }
            }

            if (!items.Any())
                return null;

            return new CompatibilityList { Items = items.Take(99).ToList() };
        }

        private static Description BuildDescription(Product product)
        {
            var description = new Description { Sections = new List<Section>() };
            var images = GetOfferImages(product);
            var imageIndex = 0;

            if (images.Any())
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new() { Type = "IMAGE", Url = images[imageIndex++] }
                    }
                });
            }

            var originalHtml = string.IsNullOrEmpty(product.SupplierName)
                ? $"<h2>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode("PRODUKT JEST ZAMIENNIKIEM"))}</h2>"
                : string.Empty;

            var nameHtml = $"<p><b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Name))}</b></p>";
            var codeHtml = string.IsNullOrWhiteSpace(product.CodeGaska)
                ? string.Empty
                : $"<p><b>Kod produktu: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.CodeGaska))}</p>";
            var producerHtml = string.IsNullOrWhiteSpace(product.SupplierName)
                ? string.Empty
                : $"<p><b>Producent: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.SupplierName))}</p>";
            var descriptionHtml = string.IsNullOrWhiteSpace(product.Description)
                ? string.Empty
                : $"<p><b>Opis: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Description))}</p>";
            var technicalHtml = string.IsNullOrWhiteSpace(product.TechnicalDetails)
                ? string.Empty
                : $"<p><b>Porady techniczne: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.TechnicalDetails))}</p>";

            var parametersHtml = string.Empty;
            if (product.Atributes?.Any() == true)
            {
                var attributesList = string.Join("",
                    product.Atributes.Select(p =>
                        $"<li>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.AttributeName))}: {RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.AttributeValue))}</li>"));
                parametersHtml = $"<p><b>Parametry/Wymiary:</b></p><ul>{attributesList}</ul>";
            }

            var package = product.Packages?.FirstOrDefault(p => p.PackRequired == 1);
            var warning = string.Empty;

            if (string.Equals(product.Unit, "MB", StringComparison.OrdinalIgnoreCase))
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode("PODANA CENA KUP TERAZ TO CENA ZA 1 METR BIEŻĄCY")}</p>";
            }

            if (package != null)
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode($"PODANA CENA KUP TERAZ TO CENA ZA 1 KOMPLET = {package.PackQty} {ConjugationHelper.Unit(Convert.ToInt32(package.PackQty), product.Unit).ToUpper()}")}</p>";
            }

            var crossNumbersText = string.Empty;
            if (product.CrossNumbers?.Any() == true)
            {
                var crossNumbers = string.Join(", ",
                    product.CrossNumbers.Select(c => System.Net.WebUtility.HtmlEncode(c.CrossNumberValue)));
                crossNumbersText = $"<p><b>Numery referencyjne: </b>{crossNumbers}</p>";
            }

            var contentBuilder = new StringBuilder()
                .Append(originalHtml)
                .Append(nameHtml)
                .Append(codeHtml)
                .Append(producerHtml)
                .Append(descriptionHtml)
                .Append(technicalHtml)
                .Append(parametersHtml)
                .Append(crossNumbersText)
                .Append(warning);

            var sectionItems = new List<SectionItem>
            {
                new() { Type = "TEXT", Content = contentBuilder.ToString() }
            };

            if (imageIndex < images.Count - 1)
            {
                sectionItems.Add(new SectionItem { Type = "IMAGE", Url = images[imageIndex++] });
            }

            description.Sections.Add(new Section { SectionItems = sectionItems });

            if (product.Applications?.Any() == true)
            {
                var applicationsByParent = product.Applications
                    .GroupBy(a => a.ParentID)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (applicationsByParent.TryGetValue(0, out var rootApps))
                {
                    string GetLeafNames(int parentId)
                    {
                        if (!applicationsByParent.ContainsKey(parentId))
                            return string.Empty;

                        var leafNames = new List<string>();
                        foreach (var child in applicationsByParent[parentId])
                        {
                            if (applicationsByParent.ContainsKey(child.ApplicationId))
                            {
                                leafNames.AddRange(GetLeafNames(child.ApplicationId)
                                    .Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries));
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
                        if (!applicationsByParent.ContainsKey(rootApp.ApplicationId))
                            continue;

                        foreach (var secondLevel in applicationsByParent[rootApp.ApplicationId])
                        {
                            var leafs = GetLeafNames(secondLevel.ApplicationId);
                            var li = $"<li><b>{System.Net.WebUtility.HtmlEncode(rootApp.Name)} - {System.Net.WebUtility.HtmlEncode(secondLevel.Name)}</b>: {leafs}</li>";
                            listItems.Add(li);
                        }
                    }

                    var appsText = $"<ul>{string.Join("", listItems)}</ul>";

                    var appSectionItems = new List<SectionItem>();
                    if (imageIndex < images.Count - 1)
                    {
                        appSectionItems.Add(new SectionItem { Type = "IMAGE", Url = images[imageIndex++] });
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
                var sectionImageItems = new List<SectionItem>
                {
                    new() { Type = "IMAGE", Url = images[imageIndex++] }
                };

                if (imageIndex < images.Count)
                {
                    sectionImageItems.Add(new SectionItem { Type = "IMAGE", Url = images[imageIndex++] });
                }

                description.Sections.Add(new Section { SectionItems = sectionImageItems });
            }

            return description;
        }

        private static decimal CalculatePrice(
            decimal priceGross,
            decimal priceNet,
            decimal minProductPriceNetForFreeDelivery,
            decimal standardDeliveryFeeNet,
            decimal bulkyDeliveryPriceNet,
            decimal customDeliveryPriceNet,
            decimal dropshippingFeeNet,
            int productType,
            int quantity,
            decimal ownMarginPercent,
            decimal marginLessThan5PLN,
            decimal marginMoreThan5PLNPercent,
            decimal marginMoreThan1000PLN)
        {
            var calculatedPrice = priceGross;

            var effectiveMargin = ownMarginPercent;
            calculatedPrice = priceGross * quantity * (1 + effectiveMargin / 100m);

            calculatedPrice += productType switch
            {
                0 => (priceNet <= minProductPriceNetForFreeDelivery ? standardDeliveryFeeNet * 1.23m : 0m),
                1 => bulkyDeliveryPriceNet * 1.23m,  // bulky
                2 => customDeliveryPriceNet * 1.23m, // custom
                _ => 0m
            };

            calculatedPrice += dropshippingFeeNet * 1.23m;

            if (calculatedPrice < 5m)
            {
                var withSmallMargin = calculatedPrice + marginLessThan5PLN;
                return withSmallMargin < 5m
                    ? withSmallMargin
                    : calculatedPrice * (1 + marginMoreThan5PLNPercent / 100m);
            }

            if (calculatedPrice <= 1000m)
            {
                var tempPrice = calculatedPrice * (1 + marginMoreThan5PLNPercent / 100m);
                if (tempPrice > 1000m)
                    return calculatedPrice + marginMoreThan1000PLN;

                return tempPrice;
            }

            return calculatedPrice + marginMoreThan1000PLN;
        }

        private static string RemoveHiddenAscii(string input) =>
            string.IsNullOrEmpty(input)
                ? input
                : new string(input.Where(c => c >= 32 || c is (char)10 or (char)13).ToArray());
    }
}