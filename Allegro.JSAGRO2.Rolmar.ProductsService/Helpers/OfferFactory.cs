using Allegro.JSAGRO2.Rolmar.ProductsService.Settings;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Helpers;
using System.Globalization;
using System.Text;

namespace Allegro.JSAGRO2.Rolmar.ProductsService.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(RolmarProduct product, AppSettings appSettings, AllegroSettings allegroSettings, PriceSettings priceSettings)
        {
            int productQuantity = (int)Math.Ceiling(product.Package);
            return new ProductOfferRequest
            {
                Name = product.Name,
                ProductSet = BuildProductSet(product, productQuantity, allegroSettings),
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
                        Amount = CalculatePrice(product, priceSettings).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = product.AllegroImages.DistinctBy(i => i.Url).Select(i => i.Url).ToList(),
                Description = BuildDescription(product),
                External = new External
                {
                    Id = product.Code
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
                        Name = GetDelivery(product, appSettings.Deliveries)
                    },
                    HandlingTime = allegroSettings.AllegroHandlingTime
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
                    Warranty = new Warranty { Name = allegroSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = allegroSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = allegroSettings.AllegroImpliedWarranty }
                },
                Parameters = BuildParameters(product.Parameters, false),
                //CompatibilityList = product.BuildCompatibilitySet ? BuildCompatibilityList(product.DefaultAllegroCategory, product.Applications, allegroCategories) : null
            };
        }

        public static ProductOfferRequest PatchOffer(AllegroOffer offer, AppSettings appSettings, AllegroSettings allegroSettings, PriceSettings priceSettings)
        {
            int productQuantity = (int)Math.Ceiling(offer.Product.Package);

            var connectedImages = offer.Product.AllegroImages?
                .Where(i => i.Connected)
                .Select(i => i.Url)
                .ToList();

            var images = connectedImages != null && connectedImages.Any()
                ? connectedImages
                : null;

            var description = images != null
                ? BuildDescription(offer.Product)
                : null;

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
                        Amount = CalculatePrice(offer.Product, priceSettings).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = images,
                Description = description,
                External = new External
                {
                    Id = offer.Product.Code
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
                        Name = GetDelivery(offer.Product, appSettings.Deliveries)
                    },
                    HandlingTime = allegroSettings.AllegroHandlingTime
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = allegroSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = allegroSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = allegroSettings.AllegroImpliedWarranty }
                },
            };
        }

        private static List<ProductSet> BuildProductSet(RolmarProduct product, int quantity, AllegroSettings allegroSettings)
        {
            var ProductSets = new List<ProductSet>();

            var Product = new ProductObject
            {
                Id = product.AllegroId,
                Images = product.AllegroImages.DistinctBy(i => i.Url).Select(i => i.Url).ToList(),
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
                    Name = allegroSettings.AllegroResponsiblePerson,
                },
                ResponsibleProducer = new ResponsibleProducer
                {
                    Type = "NAME",
                    Name = allegroSettings.AllegroResponsibleProducer,
                },
                SafetyInformation = new SafetyInformation
                {
                    Type = "TEXT",
                    Description = allegroSettings.AllegroSafetyMeasures
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

            foreach (var param in parameters.Where(p => p.IsForProduct == isForProduct))
            {
                if (string.IsNullOrWhiteSpace(param.Value))
                    continue;

                // 1. Remove all control characters (ASCII < 0x20 or 0x7F–0x9F) except space
                var cleaned = new string(param.Value
                    .Where(ch => !char.IsControl(ch) || ch == ' ')
                    .ToArray())
                    .Trim();

                List<string> values;

                if (multiValueParams.Contains(param.Name))
                {
                    // 2. Split by comma OR whitespace
                    values = cleaned
                        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    // 3. Apply max=9 for parameter id 215941
                    if (param.Name == "Numery katalogowe zamienników")
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
                        Name = param.Name,
                        Values = values
                    });
                }
            }

            return result;
        }

        private static Description BuildDescription(RolmarProduct product)
        {
            var description = new Description
            {
                Sections = new List<Section>()
            };

            int imageIndex = 0;

            // 0. First image full-width on top
            if (product.AllegroImages.Any())
            {
                product.AllegroImages = product.AllegroImages.DistinctBy(i => i.Url).ToList();
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "IMAGE",
                            Url = product.AllegroImages.Select(i => i.Url).ToList()[imageIndex++]
                        }
                    }
                });
            }

            // 0. Product header (Name + Producer + Code)
            string nameHtml = $"<p><b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Name))}</b></p>";

            string codeHtml = !string.IsNullOrWhiteSpace(product.Code)
                ? $"<p><b>Kod produktu: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Code))}</p>"
                : string.Empty;

            string producerHtml = !string.IsNullOrWhiteSpace(product.SupplierName)
                ? $"<p><b>Producent: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.SupplierName))}</p>"
                : string.Empty;

            string descriptionHtml = !string.IsNullOrWhiteSpace(product.Description) && product.Description != product.Name
                ? $"<p><b>Opis: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Description))}</p>"
                : string.Empty;

            string warning = string.Empty;

            if (string.Equals(product.Unit, "MB", StringComparison.OrdinalIgnoreCase) || string.Equals(product.Unit, "M", StringComparison.OrdinalIgnoreCase) || string.Equals(product.Unit, "METR", StringComparison.OrdinalIgnoreCase))
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode($"PODANA CENA KUP TERAZ TO CENA ZA 1 METR BIEŻĄCY")}</p>";
            }

            if (product.Package > 1)
            {
                warning = $"<p><b>UWAGA:</b> {System.Net.WebUtility.HtmlEncode($"PODANA CENA KUP TERAZ TO CENA ZA 1 KOMPLET = {product.Package} {ConjugationHelper.Unit(Convert.ToInt32(product.Package), product.Unit).ToUpper()}")}</p>";
            }

            string fitsText = string.Empty;
            if (!string.IsNullOrEmpty(product.Fits))
            {
                var fits = System.Net.WebUtility.HtmlEncode(product.Fits);
                fitsText = $"<p><b>Pasuje do: </b>{fits}</p>";
            }

            string crossNumbersText = string.Empty;
            if (!string.IsNullOrEmpty(product.Substitutes))
            {
                var crossNumbers = System.Net.WebUtility.HtmlEncode(product.Substitutes);
                crossNumbersText = $"<p><b>Symbol zamiennika: </b>{crossNumbers}</p>";
            }

            // Build the content string for text fields
            var contentBuilder = new StringBuilder();
            contentBuilder.Append(nameHtml)
                          .Append(codeHtml)
                          .Append(descriptionHtml)
                          .Append(producerHtml)
                          .Append(crossNumbersText)
                          .Append(warning);

            // Build the section
            var mainSectionItems = new List<SectionItem>
            {
                new SectionItem
                {
                    Type = "TEXT",
                    Content = contentBuilder.ToString()
                }
            };

            // Add image
            if (imageIndex < product.AllegroImages.Count)
            {
                mainSectionItems.Add(new SectionItem
                {
                    Type = "IMAGE",
                    Url = product.AllegroImages.Select(i => i.Url).ToList()[imageIndex++]
                });
            }

            description.Sections.Add(new Section
            {
                SectionItems = mainSectionItems
            });

            string parametersHtml = string.Empty;

            if (product.Specifications != null && product.Specifications.Any())
            {
                var attributesList = string.Join("",
                    product.Specifications
                        .Where(p => !((p.Name == "Opakowanie" && p.Value == "1") || (p.Name == "Opakowanie zbiorcze" && p.Value == "1")))
                        .Select(p =>
                            $"<li><b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.Name))}</b>: " +
                            $"{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.Value))} " +
                            $"{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(p.UnitName))}</li>"
                        )
                );

                if (!string.IsNullOrWhiteSpace(attributesList))
                {
                    parametersHtml = $"<p><b>Parametry/Wymiary:</b></p><ul>{attributesList}</ul>";
                }
            }

            if (!string.IsNullOrWhiteSpace(parametersHtml))
            {
                var parametersSectionItems = new List<SectionItem>();

                // Add image to parameters section
                if (imageIndex < product.AllegroImages.Count)
                {
                    parametersSectionItems.Add(new SectionItem
                    {
                        Type = "IMAGE",
                        Url = product.AllegroImages.Select(i => i.Url).ToList()[imageIndex++]
                    });
                }

                parametersSectionItems.Add(
                    new SectionItem
                    {
                        Type = "TEXT",
                        Content = parametersHtml
                    }
                );

                description.Sections.Add(new Section
                {
                    SectionItems = parametersSectionItems
                });
            }

            while (imageIndex < product.AllegroImages.Count)
            {
                var sectionImageItems = new List<SectionItem>();

                // First image
                sectionImageItems.Add(new SectionItem
                {
                    Type = "IMAGE",
                    Url = product.AllegroImages.Select(i => i.Url).ToList()[imageIndex++]
                });

                // Add second image if available
                if (imageIndex < product.AllegroImages.Count)
                {
                    sectionImageItems.Add(new SectionItem
                    {
                        Type = "IMAGE",
                        Url = product.AllegroImages.Select(i => i.Url).ToList()[imageIndex++]
                    });
                }

                description.Sections.Add(new Section
                {
                    SectionItems = sectionImageItems
                });
            }

            return description;
        }

        private static decimal CalculatePrice(RolmarProduct product, PriceSettings priceSettings)
        {
            var calculatedPrice = product.PriceGross;

            // Apply own margin
            decimal effectiveMargin = ResolveMargin(priceSettings, product.PriceGross);
            calculatedPrice = product.PriceGross * (1 + (effectiveMargin / 100m));

            // Tiered pricing rules
            if (calculatedPrice < 5m)
            {
                var withSmallMargin = calculatedPrice + priceSettings.AllegroMarginUnder5PLN;
                calculatedPrice = withSmallMargin < 5m
                    ? withSmallMargin
                    : calculatedPrice * (1 + priceSettings.AllegroMarginBetween5and1000PLNPercent / 100m);
            }
            else if (calculatedPrice <= 1000m)
            {
                var tempPrice = calculatedPrice * (1 + priceSettings.AllegroMarginBetween5and1000PLNPercent / 100m);
                calculatedPrice = tempPrice > 1000m
                    ? calculatedPrice + priceSettings.AllegroMarginMoreThan1000PLN
                    : tempPrice;
            }
            else
            {
                calculatedPrice += priceSettings.AllegroMarginMoreThan1000PLN;
            }

            // ----- New step: Add DPD shipping cost -----
            decimal shippingCost = 0m;

            if (calculatedPrice < 30m)
                shippingCost = Math.Min(1.99m, Math.Round((calculatedPrice / 30m) * 1.99m, 2));
            else if (calculatedPrice >= 30m && calculatedPrice <= 44.99m)
                shippingCost = 1.99m;
            else if (calculatedPrice >= 45m && calculatedPrice <= 64.99m)
                shippingCost = 3.99m;
            else if (calculatedPrice >= 65m && calculatedPrice <= 99.99m)
                shippingCost = 5.79m;
            else if (calculatedPrice >= 100m && calculatedPrice <= 149.99m)
                shippingCost = 9.09m;
            else if (calculatedPrice >= 150m)
                shippingCost = 11.49m;

            calculatedPrice += shippingCost;

            return Math.Max(calculatedPrice, 1.00m);
        }

        private static decimal ResolveMargin(PriceSettings priceSettings, decimal grossPrice)
        {
            var range = priceSettings.MarginRanges
                .FirstOrDefault(r => grossPrice >= r.Min && grossPrice <= r.Max);

            if (range == null)
                return priceSettings.MarginRanges.Last().Margin;

            return range.Margin;
        }

        private static string GetDelivery(RolmarProduct product, List<DeliverySettings> deliveries)
        {
            if (deliveries == null || deliveries.Count == 0)
                return null;

            var productWeight = (decimal)product.Weight;

            var length = GetDimensionCm(product, "Długość");
            var width = GetDimensionCm(product, "Szerokość");
            var height = GetDimensionCm(product, "Wysokość");

            var matchingDelivery = deliveries
                .Where(d =>
                    d.Weight >= productWeight &&
                    (length == null || d.Length >= length) &&
                    (width == null || d.Width >= width) &&
                    (height == null || d.Height >= height))
                .OrderBy(d => d.Weight)
                .ThenBy(d => d.Length * d.Width * d.Height) // smallest volume wins
                .FirstOrDefault();

            // Fallback: biggest delivery
            return matchingDelivery?.DeliveryName
                ?? deliveries
                    .OrderByDescending(d => d.Weight)
                    .ThenByDescending(d => d.Length * d.Width * d.Height)
                    .First()
                    .DeliveryName;
        }

        private static decimal? GetDimensionCm(RolmarProduct product, string dimensionName)
        {
            var spec = product.Specifications?
                .FirstOrDefault(s =>
                    string.Equals(s.Name, dimensionName, StringComparison.OrdinalIgnoreCase));

            if (spec == null)
                return null;

            if (!decimal.TryParse(
                    spec.Value.Replace(",", "."),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var value))
                return null;

            return spec.UnitName?.ToLower() switch
            {
                "mm" => value / 10m,
                "cm" => value,
                "m" => value * 100m,
                _ => null
            };
        }

        private static string RemoveHiddenAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Remove ASCII control characters except newline (10) and carriage return (13)
            return new string(input.Where(c => c >= 32 || c == 10 || c == 13).ToArray());
        }
    }
}