using JSAGROSyncServices.Products.Settings;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Helpers;
using JSAGROSyncServices.Contracts.Interfaces;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace JSAGROSyncServices.Products.Helpers
{
    public class RolmarOfferFactory : IOfferFactory
    {
        private readonly AppSettings _appSettings;
        private readonly AllegroSettings _allegroSettings;
        private readonly PriceSettings _priceSettings;

        public RolmarOfferFactory(
            IOptions<AppSettings> appSettings,
            IOptions<AllegroSettings> allegroSettings,
            IOptions<PriceSettings> priceSettings)
        {
            _appSettings = appSettings.Value;
            _allegroSettings = allegroSettings.Value;
            _priceSettings = priceSettings.Value;
        }

        public Task PrepareAsync(CancellationToken ct) => Task.CompletedTask;

        /// <summary>
        /// Oferty Rolmaru wystawiamy wyłącznie na istniejącym produkcie z katalogu Allegro.
        /// Propozycja nowego produktu wymaga kompletu parametrów produktowych, których dane
        /// dostawcy nie zawierają - kończyła się błędem "Uzupełnij parametry obowiązkowe"
        /// i oferta i tak nie powstawała, a próba wracała w każdym cyklu.
        /// </summary>
        public bool RequiresCatalogProduct => true;

        public decimal CalculatePrice(RolmarProduct product) => CalculatePrice(product, _priceSettings, _appSettings.Deliveries);

        public ProductOfferRequest BuildOffer(RolmarProduct product)
        {
            var appSettings = _appSettings;
            var allegroSettings = _allegroSettings;
            var priceSettings = _priceSettings;

            int productQuantity = (int)Math.Ceiling(product.Package);
            return new ProductOfferRequest
            {
                Name = BuildOfferName(product),
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
                        Amount = CalculatePrice(product, priceSettings, appSettings.Deliveries).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = GalleryImages(product),
                Description = BuildDescription(product, GalleryImages(product)),
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
                        Name = DeliveryMatcher.Match(product, appSettings.Deliveries)
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
                TaxSettings = new()
                {
                    Rates = new List<Rate>
                    {
                        new Rate
                        {
                            RateValue = "23.00",
                            CountryCode = "PL"
                        }
                    },
                    Subject = "GOODS"
                },
                AfterSalesServices = new AfterSalesServices
                {
                    Warranty = new Warranty { Name = allegroSettings.AllegroWarranty },
                    ReturnPolicy = new ReturnPolicy { Name = allegroSettings.AllegroReturnPolicy },
                    ImpliedWarranty = new ImpliedWarranty { Name = allegroSettings.AllegroImpliedWarranty }
                },
                Parameters = BuildParameters(product.Parameters, isForProduct: false)
            };
        }

        private const int MinOfferNameLength = 12;
        private const int MinOfferNameWords = 3;
        private const int MaxOfferNameLength = 75;

        /// <summary>Maksymalna długość pojedynczego słowa w tytule oferty według Allegro.</summary>
        private const int MaxOfferNameWordLength = 30;

        /// <summary>Do ilu znaków przycinamy słowo, które przekracza limit.</summary>
        private const int ShortenedWordLength = 20;

        /// <summary>Maksymalna liczba zdjęć w galerii oferty Allegro.</summary>
        private const int MaxGalleryImages = 16;

        /// <summary>
        /// Allegro wymaga tytulu o dlugosci min. 12 znakow i min. 3 slowach. Krotkie nazwy od dostawcy
        /// (np. "Wspornik Bizon") uzupelniamy kodem produktu - bez tego oferta jest odrzucana w kazdym cyklu.
        /// </summary>
        private static string BuildOfferName(RolmarProduct product)
        {
            var name = (product.Name ?? string.Empty).Trim();
            var code = (product.Code ?? string.Empty).Trim();

            if (!IsOfferNameValid(name) && code.Length > 0 && !name.Contains(code, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Length == 0 ? code : name + " " + code;
            }

            name = ShortenLongWords(name);

            return name.Length > MaxOfferNameLength ? name.Substring(0, MaxOfferNameLength).TrimEnd() : name;
        }

        /// <summary>
        /// Allegro odrzuca tytuł, w którym pojedyncze słowo ma ponad 30 znaków (zwykle sklejone
        /// numery katalogowe). Takie słowo przycinamy, resztę tytułu zostawiamy bez zmian.
        /// </summary>
        private static string ShortenLongWords(string name)
        {
            if (name.Length == 0)
                return name;

            var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Length > MaxOfferNameWordLength)
                    words[i] = words[i].Substring(0, ShortenedWordLength);
            }

            return string.Join(' ', words);
        }

        private static bool IsOfferNameValid(string name)
        {
            if (name.Length < MinOfferNameLength)
                return false;

            return name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length >= MinOfferNameWords;
        }

        public ProductOfferRequest PatchOffer(AllegroOffer offer, bool keepCurrentPrice)
        {
            var appSettings = _appSettings;
            var allegroSettings = _allegroSettings;
            var priceSettings = _priceSettings;

            // Oferta bez wczytanego produktu nie ma jak zostać zaktualizowana - lepiej głośny błąd niż cicha awaria.
            var product = offer.Product
                ?? throw new InvalidOperationException($"Oferta {offer.Id} nie ma wczytanego produktu.");

            // Oferta z cennika zarządzanego ręcznie: nie ruszamy ceny, cennika dostawy ani czasu realizacji.
            var manuallyManagedDelivery = PriceHelper.IsManuallyManagedDelivery(offer.DeliveryName, appSettings.DeliveriesWithoutPriceUpdate);

            int productQuantity = (int)Math.Ceiling(product.Package);

            var connectedImages = product.AllegroImages?
                .Where(i => i.Connected)
                .Select(i => i.Url)
                .Distinct()
                .Take(MaxGalleryImages)
                .ToList();

            // Zdjęcia wgrane w tym cyklu (po resecie galerii) nie są jeszcze oznaczone jako podpięte.
            // Bez nich patch nie miał ani galerii, ani opisu, a Allegro zostawiało stary opis ze
            // zdjęciami spoza nowej galerii ("GalleryPhotosInStandardizedDescription").
            var images = connectedImages != null && connectedImages.Any()
                ? connectedImages
                : product.AllegroImages?.Count > 0 ? GalleryImages(product) : null;

            var description = images != null
                ? BuildDescription(product, images)
                : null;

            return new ProductOfferRequest
            {
                // Bez productSet Allegro odrzuca patch: nie widzi podpietego produktu,
                // producenta odpowiedzialnego ani informacji o bezpieczenstwie (GPSR).
                ProductSet = BuildProductSet(product, productQuantity, allegroSettings, offer.ProductId),
                // Kategorii oferty nie zmieniamy: po przeniesieniu parametry oferty z poprzedniej kategorii
                // były w nowej nieprawidłowe (InvalidParameterIdsInCategory), a kategorii ogólnej Allegro nie
                // przyjmuje (IllegalChangeCategoryId). Produkt z katalogu dobieramy tak, żeby pasował do
                // kategorii wszystkich ofert produktu - zob. AllegroProductService.FindCatalogProduct.
                Stock = new Stock
                {
                    Available = Convert.ToInt32(Math.Floor(product.InStock)),
                    Unit = MapAllegroUnit(product.Unit)
                },
                // Gdy ceny nie aktualizujemy (cennik zarządzany ręcznie albo zbyt duży spadek ceny),
                // nie wysyłamy sekcji sellingMode w ogóle. Wysłanie ceny ze snapshotu z bazy
                // cofałoby ręcznie ustawioną cenę, gdyby ktoś zmienił ją na Allegro w trakcie cyklu.
                SellingMode = keepCurrentPrice || manuallyManagedDelivery
                    ? null
                    : new SellingMode
                    {
                        Format = "BUY_NOW",
                        Price = new Price
                        {
                            Amount = CalculatePrice(product, priceSettings, appSettings.Deliveries).ToString("F2", CultureInfo.InvariantCulture),
                            Currency = "PLN"
                        }
                    },
                Images = images,
                Description = description,
                // Wyłącznie obowiązkowe parametry oferty (np. "Stan") - parametry produktu daje produkt z katalogu.
                Parameters = NullIfEmpty(BuildParameters(product.Parameters, isForProduct: false)),
                External = new External
                {
                    Id = product.Code
                },
                Publication = new Publication
                {
                    Status = product.InStock >= appSettings.MinProductStock ? "ACTIVE" : "ENDED",
                    StartingAt = offer.Status == "INACTIVE" ? DateTime.UtcNow : null
                },
                TaxSettings = new()
                {
                    Rates = new List<Rate>
                    {
                        new Rate
                        {
                            RateValue = "23.00",
                            CountryCode = "PL"
                        }
                    },
                    Subject = "GOODS"
                },
                // Całą sekcję dostawy pomijamy - inaczej nadpisalibyśmy ręcznie ustawiony czas realizacji.
                Delivery = manuallyManagedDelivery
                    ? null
                    : new Delivery
                    {
                        ShippingRates = new ShippingRates { Name = DeliveryMatcher.Match(product, appSettings.Deliveries) },
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

        /// <summary>
        /// Produkt z katalogu Allegro - wysyłamy wyłącznie jego id. Nazwa, zdjęcia czy parametry
        /// obok id to dla Allegro propozycja zmiany produktu, a wtedy wymaga kompletu parametrów
        /// produktowych - stąd "Uzupełnij parametry obowiązkowe" mimo podpiętego produktu.
        /// Oferta bez dopasowanego produktu tu nie dociera: odsiewa ją <see cref="RequiresCatalogProduct"/>.
        /// </summary>
        private static List<ProductSet> BuildProductSet(RolmarProduct product, int quantity, AllegroSettings allegroSettings, string? offerProductId = null)
        {
            var ProductSets = new List<ProductSet>();

            // Id produktu z katalogu: zapamiętane przy produkcie, a dopiero bez niego to z oferty. Id na
            // ofercie przychodzi z Allegro w każdym cyklu i po podmianie niekompletnego produktu wciąż
            // wskazuje stary - patch z nim wracałby z "Uzupełnij parametry obowiązkowe".
            var catalogProductId = string.IsNullOrWhiteSpace(product.AllegroId) ? offerProductId : product.AllegroId;

            ProductSets.Add(new ProductSet
            {
                // Parametry produktu pochodzą wyłącznie z produktu z katalogu - nie wysyłamy własnych.
                // Gdy katalogowy nie ma kompletu obowiązkowych, serwis ofert podpina inny albo kończy ofertę.
                ProductObject = new ProductObject
                {
                    Id = catalogProductId
                },
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

        private static List<Parameter>? NullIfEmpty(List<Parameter> parameters) => parameters.Count > 0 ? parameters : null;

        /// <summary>
        /// Parametry oferty. W bazie Rolmar trzyma wyłącznie obowiązkowe parametry oferty
        /// (krok parametrów pomija parametry produktu), a produktowych nigdy nie wysyłamy.
        /// </summary>
        private static List<Parameter> BuildParameters(ICollection<ProductParameter> parameters, bool isForProduct)
        {
            var result = new List<Parameter>();

            // parameters that should support multiple values
            var multiValueParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "numery katalogowe zamienników", "marka",
            };

            foreach (var param in parameters.Where(p => p.IsForProduct == isForProduct
                                                        && p.Name != "EAN (GTIN)"
                                                        && p.Name != "Informacje o bezpieczeństwie"))
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
                    // 2. Numery dzielimy po przecinkach i spacjach, marki tylko po przecinkach
                    values = cleaned
                        .Split(ParameterFallbacks.MultiValueSeparators(param.Name), StringSplitOptions.RemoveEmptyEntries)
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

        /// <summary>Zdjęcia galerii: bez powtórzeń i najwyżej tyle, ile przyjmuje Allegro.</summary>
        private static List<string> GalleryImages(RolmarProduct product) =>
            product.AllegroImages.Select(i => i.Url).Distinct().Take(MaxGalleryImages).ToList();

        /// <summary>
        /// Opis używa wyłącznie zdjęć z galerii. Zdjęcie z opisu, którego nie ma w galerii, Allegro
        /// dokłada do oferty - przy produktach z ponad 16 zdjęciami kończyło się to błędem limitu zdjęć.
        /// </summary>
        private static Description BuildDescription(RolmarProduct product, IReadOnlyList<string> images)
        {
            var description = new Description
            {
                Sections = new List<Section>()
            };

            int imageIndex = 0;

            // 0. First image full-width on top
            if (images.Count > 0)
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

            string codeHtml = !string.IsNullOrWhiteSpace(product.Code)
                ? $"<p><b>Kod produktu: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.Code))}</p>"
                : string.Empty;

            string producerHtml = !string.IsNullOrWhiteSpace(product.SupplierName)
                ? $"<p><b>Producent: </b>{RemoveHiddenAscii(System.Net.WebUtility.HtmlEncode(product.SupplierName))}</p>"
                : string.Empty;

            // Opis od Rolmara bywa sformatowany HTML-em (<b>, <br />) - zamieniamy go na akapity i listy,
            // zamiast kodować całość i pokazywać kupującemu surowe tagi.
            string descriptionHtml = !string.IsNullOrWhiteSpace(product.Description) && product.Description != product.Name
                ? SupplierHtml.ToLabelledBlocks("Opis:", RemoveHiddenAscii(product.Description))
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
                fitsText = SupplierHtml.ToLabelledBlocks("Pasuje do:", product.Fits);
            }

            string crossNumbersText = string.Empty;
            if (!string.IsNullOrEmpty(product.Substitutes))
            {
                crossNumbersText = SupplierHtml.ToLabelledBlocks("Symbol zamiennika:", product.Substitutes);
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
            if (imageIndex < images.Count)
            {
                mainSectionItems.Add(new SectionItem
                {
                    Type = "IMAGE",
                    Url = images[imageIndex++]
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
                if (imageIndex < images.Count)
                {
                    parametersSectionItems.Add(new SectionItem
                    {
                        Type = "IMAGE",
                        Url = images[imageIndex++]
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

        public static decimal CalculatePrice(RolmarProduct product, PriceSettings priceSettings, List<DeliverySettings> deliveries)
        {
            var calculatedPrice = product.PriceGross;

            // Apply own margin
            decimal effectiveMargin = ResolveMargin(priceSettings, product.PriceGross);
            calculatedPrice = product.PriceGross * (1 + (effectiveMargin / 100m));

            return OfferPricing.Finalize(
                calculatedPrice,
                priceSettings,
                ProductDimensions.IsOversized(product, priceSettings.OversizeThresholdCm),
                chargeShipping: DeliveryMatcher.MatchDelivery(product, deliveries)?.IsSmart == true);
        }

        private static decimal ResolveMargin(PriceSettings priceSettings, decimal grossPrice)
        {
            var range = priceSettings.MarginRanges
                .FirstOrDefault(r => grossPrice >= r.Min && grossPrice <= r.Max);

            if (range == null)
                return priceSettings.MarginRanges.Last().Margin;

            return range.Margin;
        }

        private static string RemoveHiddenAscii(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            // Remove ASCII control characters except newline (10) and carriage return (13)
            return new string(input.Where(c => c >= 32 || c == 10 || c == 13).ToArray());
        }
    }
}