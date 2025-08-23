using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Models.Product;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Helpers
{
    public static class OfferFactory
    {
        public static ProductOfferRequest BuildOffer(Models.Product.Product product)
        {
            return new ProductOfferRequest
            {
                Name = product.Name,
                ProductSet = BuildProductSet(product),
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
                        Amount = product.PriceNet.ToString("F2", CultureInfo.InvariantCulture),
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
                    Status = "INACTIVE",
                    Duration = "PT702H",
                    StartingAt = DateTime.UtcNow,
                    Republish = true
                },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates
                    {
                        Name = "JAG API"
                    },
                    HandlingTime = "PT24H",
                    AdditionalInfo = "Przesyłki realizowane są w dni robocze od poniedziałku do piątku w godzinach 8:00 - 16:00.",
                    ShipmentDate = GetShipmentDate()
                },
                Location = new Location
                {
                    City = "Bielsk Podlaski",
                    CountryCode = "PL",
                    PostCode = "17-100",
                    Province = "Podlaskie"
                },
                Payments = new Payments
                {
                    Invoice = "VAT"
                },
                //AfterSalesServices = new AfterSalesServices
                //{
                //    Warranty = new Warranty { Id = "default" },
                //    ReturnPolicy = new ReturnPolicy { Id = "default" }
                //},
                Parameters = BuildParameters(product.Parameters, false),
                CompatibilityList = BuildCompatibilityList(product.Applications)
            };
        }

        private static List<ProductSet> BuildProductSet(Product product)
        {
            var ProductSets = new List<ProductSet>();

            var Product = new ProductObject
            {
                Name = product.Name,
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
                    Name = "JS-AGRO",
                },
                ResponsibleProducer = new ResponsibleProducer
                {
                    Type = "NAME",
                    Name = "Gąska sp. z o.o.",
                },
                SafetyInformation = new SafetyInformation
                {
                    Type = "TEXT",
                    Description = "Tekst bezpieczeństwa: \r\nLista ostrzeżeń dotyczących bezpieczeństwa części do kombajnów oparta o wymagania Rozporządzenia (UE) 2023/988 w sprawie ogólnego bezpieczeństwa produktów (GPSR):\r\n\r\n1.  Ostre krawędzie i elementy: Podczas montażu, demontażu i użytkowania części, zachowaj szczególną ostrożność ze względu na ostre krawędzie i wystające elementy, które mogą powodować skaleczenia. Używaj rękawic ochronnych.\r\n2.  Waga i stabilność: Nie przeciążaj kombajnu. Upewnij się, że każda część jest odpowiednio zamocowana i wyważona, aby uniknąć przewrócenia się maszyny.\r\n3.  Materiały łatwopalne: Unikaj używania otwartego ognia lub palenia w pobliżu części kombajnu. Przechowuj i używaj smarów, olejów i paliw z dala od źródeł ciepła i ognia.\r\n4.  Wysokie temperatury: Uważaj na gorące powierzchnie części, które mogą spowodować oparzenia. Odczekaj, aż ostygną przed dotknięciem.\r\n5.  Ciśnienie hydrauliczne: Przed odłączeniem przewodów hydraulicznych upewnij się, że ciśnienie w układzie zostało zredukowane. Wyciekający płyn hydrauliczny może być niebezpieczny dla skóry i oczu.\r\n6.  Zagrożenia mechaniczne: Upewnij się, że wszystkie osłony i zabezpieczenia są na swoim miejscu i w dobrym stanie, aby zapobiec kontaktowi z ruchomymi częściami maszyny. Wyłącz kombajn i poczekaj na zatrzymanie wszystkich ruchomych elementów przed rozpoczęciem jakiejkolwiek konserwacji lub naprawy.\r\n7.  Elektryczność: Przed pracami przy instalacji elektrycznej kombajnu, odłącz zasilanie, aby uniknąć porażenia prądem.\r\n8.  Ochrona słuchu: Podczas pracy z kombajnem używaj ochronników słuchu, aby zminimalizować ryzyko uszkodzenia słuchu.\r\n9.  Toksyczne substancje: Unikaj wdychania pyłów, oparów i gazów powstających podczas pracy kombajnu i konserwacji części. Zapewnij odpowiednią wentylację.\r\n10. Niebezpieczne środowisko: Zachowaj ostrożność podczas pracy w pobliżu linii energetycznych, rowów lub na nierównym terenie.\r\n11. Uszkodzenia strukturalne: Regularnie kontroluj stan części pod kątem korozji, pęknięć i innych uszkodzeń, które mogą osłabić strukturę i spowodować awarię. Uszkodzone elementy wymień niezwłocznie.\r\n12. Bezpieczeństwo dzieci i osób postronnych: Upewnij się, że dzieci i osoby postronne znajdują się w bezpiecznej odległości od pracującego kombajnu."
                },
            });

            return ProductSets;
        }

        public static DateTime GetShipmentDate()
        {
            var now = DateTime.Now;
            DateTime shipmentLocal;

            if (now.Hour >= 16)
            {
                // After 16:00 → add 1 day
                shipmentLocal = now.AddDays(1);
            }
            else
            {
                // Before 16:00 → today 23:59
                shipmentLocal = new DateTime(now.Year, now.Month, now.Day, 23, 59, 0);
            }

            // Convert to UTC
            return shipmentLocal.ToUniversalTime();
        }

        private static string MapAllegroUnit(string productUnit)
        {
            if (string.IsNullOrWhiteSpace(productUnit))
                return "UNIT"; // default

            productUnit = productUnit.ToLower();

            if (productUnit == "szt")
                return "UNIT";
            else if (productUnit == "m" || productUnit == "mb")
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

            foreach (var param in parameters.Where(p => p.IsForProduct == isForProduct))
            {
                List<string> values;

                if (!string.IsNullOrEmpty(param.Value) && multiValueParams.Contains(param.CategoryParameter.Name))
                {
                    values = param.Value
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
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

        public static CompatibilityList BuildCompatibilityList(IEnumerable<Application> applications)
        {
            if (applications == null)
                return new CompatibilityList { Items = new List<Item>() };

            // find leaf nodes (no children)
            var leafApps = applications
                .Where(a => !applications.Any(child => child.ParentID == a.ApplicationId))
                .ToList();

            return new CompatibilityList
            {
                Items = leafApps.Select(a => new Item
                {
                    Type = "TEXT",
                    Text = a.Name
                }).ToList()
            };
        }

        private static Description BuildDescription(Models.Product.Product product)
        {
            var description = new Description
            {
                Sections = new List<Section>()
            };

            var images = product.Images?.ToList() ?? new List<ProductImage>();
            int imageIndex = 0;

            // 1. First image full-width on top
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

            // 2. Description
            if (!string.IsNullOrWhiteSpace(product.Description))
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "TEXT",
                            Content = $"<p><b>Opis: </b>{System.Net.WebUtility.HtmlEncode(product.Description)}</p>"
                        }
                    }
                });
            }

            // 3. Technical details
            if (!string.IsNullOrWhiteSpace(product.TechnicalDetails))
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "TEXT",
                            Content = $"<p><b>Porady techniczne: </b>{System.Net.WebUtility.HtmlEncode(product.TechnicalDetails)}</p>"
                        }
                    }
                });
            }

            // 4. Attributes/parameters
            if (product.Parameters != null && product.Parameters.Any())
            {
                var paramText = string.Join(", ", product.Atributes.Select(p => $"{System.Net.WebUtility.HtmlEncode(p.AttributeName)}: {System.Net.WebUtility.HtmlEncode(p.AttributeValue)}"));

                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "TEXT",
                            Content = $"<p><b>Parametry: </b>{paramText}</p>"
                        }
                    }
                });
            }

            // 5. Cross numbers (with image if available)
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
                        Url = images[imageIndex++].AllegroUrl,
                    });
                }

                description.Sections.Add(new Section { SectionItems = sectionItems });
            }

            // 6. Applications (with image if available)
            if (product.Applications != null && product.Applications.Any())
            {
                var applicationsByParent = product.Applications
                    .GroupBy(a => a.ParentID)
                    .ToDictionary(g => g.Key, g => g.ToList());

                if (applicationsByParent.ContainsKey(0))
                {
                    var rootApps = applicationsByParent[0];

                    string BuildApplicationText(List<Application> apps, int depth = 0)
                    {
                        if (apps == null || !apps.Any())
                            return string.Empty;

                        var sb = new StringBuilder();

                        foreach (var app in apps)
                        {
                            // Indent using spaces for hierarchy
                            sb.Append("<p>");
                            sb.Append(new string('-', depth * 2)); // optional: prefix with dashes
                            sb.Append(System.Net.WebUtility.HtmlEncode(app.Name));
                            sb.Append("</p>");

                            if (applicationsByParent.ContainsKey(app.ApplicationId))
                            {
                                sb.Append(BuildApplicationText(applicationsByParent[app.ApplicationId], depth + 1));
                            }
                        }

                        return sb.ToString();
                    }

                    var sectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "TEXT",
                            Content = "<p><b>Zastosowanie:</b></p>" + BuildApplicationText(rootApps)
                        }
                    };

                    // Optional image
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
            }

            // 7. Remaining images at the bottom
            while (imageIndex < images.Count)
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new SectionItem
                        {
                            Type = "TEXT",
                            Content = "<p><b>Zdjęcie produktu:</b></p>"
                        },
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
    }
}