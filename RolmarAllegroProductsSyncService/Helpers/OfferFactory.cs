using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Helpers;
using JSAGROSyncServices.Shared.Models;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Settings;
using System;
using System.Globalization;
using System.Text;

namespace RolmarAllegroProductsSyncService.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(RolmarProduct product, AppSettings appSettings)
        {
            int productQuantity = (int)Math.Ceiling(product.Package);
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
                        Amount = (CalculatePrice(product.PriceGross, productQuantity, appSettings.OwnMarginPercent, appSettings.AllegroMarginUnder5PLN, appSettings.OwnMarginPercentUnder10PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN) * 10).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = product.AllegroImages.Select(i => i.Url).ToList(),
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
                        Name = appSettings.AllegroDeliveryName
                    },
                    HandlingTime = appSettings.AllegroHandlingTime
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
                //CompatibilityList = product.BuildCompatibilitySet ? BuildCompatibilityList(product.DefaultAllegroCategory, product.Applications, allegroCategories) : null
            };
        }

        public static ProductOfferRequest PatchOffer(AllegroOffer offer, AppSettings appSettings)
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
                        Amount = (CalculatePrice(offer.Product.PriceGross, productQuantity, appSettings.OwnMarginPercent, appSettings.OwnMarginPercentUnder10PLN, appSettings.AllegroMarginUnder5PLN, appSettings.AllegroMarginBetween5and1000PLNPercent, appSettings.AllegroMarginMoreThan1000PLN) * 10).ToString("F2", CultureInfo.InvariantCulture),
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
                        Name = appSettings.AllegroDeliveryName
                    },
                    HandlingTime = appSettings.AllegroHandlingTime
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = appSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = appSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = appSettings.AllegroImpliedWarranty }
                },
            };
        }

        private static List<ProductSet> BuildProductSet(RolmarProduct product, int quantity, AppSettings appSettings)
        {
            var ProductSets = new List<ProductSet>();

            var Product = new ProductObject
            {
                Id = product.AllegroId,
                Images = product.AllegroImages.Select(i => i.Url).ToList(),
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

        private static decimal CalculatePrice(
            decimal initialPrice,
            int quantity,
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