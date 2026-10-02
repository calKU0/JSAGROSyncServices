using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Helpers;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Budowanie ofert z produktów Inter Cars. W odróżnieniu od pozostałych dostawców Inter Cars
    /// nie udostępnia zdjęć - oferta bierze je z produktu w katalogu Allegro, a galerię i sekcje
    /// graficzne opisu pomijamy. Produkty Inter Cars nie mają też zastosowań ani opakowań zbiorczych,
    /// więc oferta jest prostsza niż u Gąski czy Rolmara.
    /// </summary>
    public class InterCarsOfferFactory : IOfferFactory
    {
        private const int MinOfferNameLength = 12;
        private const int MinOfferNameWords = 3;
        private const int MaxOfferNameLength = 75;

        /// <summary>Maksymalna długość pojedynczego słowa w tytule oferty według Allegro.</summary>
        private const int MaxOfferNameWordLength = 30;

        /// <summary>Do ilu znaków przycinamy słowo, które przekracza limit.</summary>
        private const int ShortenedWordLength = 20;

        /// <summary>Maksymalna liczba zdjęć w galerii oferty Allegro.</summary>
        private const int MaxGalleryImages = 16;

        private readonly AppSettings _appSettings;
        private readonly AllegroSettings _allegroSettings;
        private readonly PriceSettings _priceSettings;

        public InterCarsOfferFactory(
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
        /// Oferty Inter Cars wystawiamy wyłącznie na istniejącym produkcie z katalogu Allegro.
        /// Katalog dostawcy nie zawiera parametrów, których Allegro wymaga do utworzenia produktu
        /// (ani ich nazw, ani wartości słownikowych), więc propozycja nowego produktu kończyła się
        /// błędem "Uzupełnij parametry obowiązkowe" i oferta i tak nie powstawała.
        /// </summary>
        public bool RequiresCatalogProduct => true;

        /// <summary>
        /// Zdjęć Inter Carsu nie pobieramy ani nie wysyłamy - galerię bierze Allegro z produktu
        /// w katalogu, pod który oferta jest podpięta.
        /// </summary>
        public bool UsesOwnImages => false;

        public decimal CalculatePrice(RolmarProduct product) => CalculatePrice(product, _priceSettings, _appSettings.Deliveries);

        public ProductOfferRequest BuildOffer(RolmarProduct product)
        {
            return new ProductOfferRequest
            {
                Name = BuildOfferName(product),
                ProductSet = BuildProductSet(product),
                Category = new Category { Id = product.DefaultAllegroCategory.ToString(CultureInfo.InvariantCulture) },
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
                        Amount = CalculatePrice(product, _priceSettings, _appSettings.Deliveries).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Description = BuildDescription(product),
                External = new External { Id = product.Code },
                Publication = new Publication
                {
                    Status = "ACTIVE",
                    StartingAt = DateTime.UtcNow
                },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates { Name = DeliveryMatcher.Match(product, _appSettings.Deliveries) },
                    HandlingTime = _allegroSettings.AllegroHandlingTime
                },
                Location = new Location
                {
                    City = "Bielsk Podlaski",
                    CountryCode = "PL",
                    PostCode = "17-100",
                    Province = "PODLASKIE"
                },
                Payments = new Payments { Invoice = "VAT" },
                TaxSettings = BuildTaxSettings(),
                AfterSalesServices = BuildAfterSalesServices(),
                Parameters = BuildOfferParameters(product.Parameters)
            };
        }

        public ProductOfferRequest PatchOffer(AllegroOffer offer, bool keepCurrentPrice)
        {
            // Oferta bez wczytanego produktu nie ma jak zostać zaktualizowana - lepiej głośny błąd niż cicha awaria.
            var product = offer.Product
                ?? throw new InvalidOperationException($"Oferta {offer.Id} nie ma wczytanego produktu.");

            // Oferta z cennika zarządzanego ręcznie: nie ruszamy ceny, cennika dostawy ani czasu realizacji.
            var manuallyManagedDelivery = PriceHelper.IsManuallyManagedDelivery(offer.DeliveryName, _appSettings.DeliveriesWithoutPriceUpdate);

            return new ProductOfferRequest
            {
                // Bez productSet Allegro odrzuca patch: nie widzi podpietego produktu,
                // producenta odpowiedzialnego ani informacji o bezpieczenstwie (GPSR).
                //Name = BuildOfferName(product),
                ProductSet = BuildProductSet(product, null),
                Category = new Category { Id = product.DefaultAllegroCategory.ToString(CultureInfo.InvariantCulture) },
                Stock = new Stock
                {
                    Available = Convert.ToInt32(Math.Floor(product.InStock)),
                    Unit = MapAllegroUnit(product.Unit)
                },
                // Gdy ceny nie aktualizujemy (cennik zarządzany ręcznie albo zbyt duży spadek ceny),
                // nie wysyłamy sekcji sellingMode w ogóle - inaczej cofnęlibyśmy cenę ustawioną ręcznie.
                SellingMode = keepCurrentPrice || manuallyManagedDelivery
                    ? null
                    : new SellingMode
                    {
                        Format = "BUY_NOW",
                        Price = new Price
                        {
                            Amount = CalculatePrice(product, _priceSettings, _appSettings.Deliveries).ToString("F2", CultureInfo.InvariantCulture),
                            Currency = "PLN"
                        }
                    },
                // Nazwy nie wysylamy: na Allegro jest zarzadzana recznie.
                // Galerii tez nie - pokazuje ja produkt z katalogu.
                Description = BuildDescription(product),
                External = new External { Id = product.Code },
                Publication = new Publication
                {
                    Status = product.InStock >= _appSettings.MinProductStock ? "ACTIVE" : "ENDED",
                    StartingAt = offer.Status == "INACTIVE" ? DateTime.UtcNow : null
                },
                TaxSettings = BuildTaxSettings(),
                // Całą sekcję dostawy pomijamy - inaczej nadpisalibyśmy ręcznie ustawiony czas realizacji.
                Delivery = manuallyManagedDelivery
                    ? null
                    : new Delivery
                    {
                        ShippingRates = new ShippingRates { Name = DeliveryMatcher.Match(product, _appSettings.Deliveries) },
                        HandlingTime = _allegroSettings.AllegroHandlingTime
                    },
                AfterSalesServices = BuildAfterSalesServices()
            };
        }

        // ---------------------------------------------------------------- budowa żądania

        private static List<string>? GalleryImages(RolmarProduct product)
        {
            var images = product.AllegroImages
                .DistinctBy(i => i.Url)
                .Select(i => i.Url)
                .Take(MaxGalleryImages)
                .ToList();

            return images.Count > 0 ? images : null;
        }

        /// <summary>
        /// Produkt z katalogu Allegro - wysyłamy wyłącznie jego id. Nazwa, zdjęcia czy parametry
        /// obok id to dla Allegro propozycja zmiany produktu, a wtedy wymaga kompletu parametrów
        /// produktowych - stąd "Uzupełnij parametry obowiązkowe" mimo podpiętego produktu.
        /// Oferta bez dopasowanego produktu tu nie dociera: odsiewa ją <see cref="RequiresCatalogProduct"/>.
        /// </summary>
        private List<ProductSet> BuildProductSet(RolmarProduct product, string? offerProductId = null)
        {
            // Id produktu z katalogu: najpierw to, które oferta już ma, potem zapamiętane przy produkcie.
            var catalogProductId = string.IsNullOrWhiteSpace(offerProductId) ? product.AllegroId : offerProductId;

            return new List<ProductSet>
            {
                new()
                {
                    ProductObject = new ProductObject { Id = catalogProductId },
                    Quantity = new Quantity { Value = Math.Max((int)Math.Ceiling(product.Package), 1) },
                    ResponsiblePerson = new ResponsiblePerson { Name = _allegroSettings.AllegroResponsiblePerson },
                    ResponsibleProducer = new ResponsibleProducer
                    {
                        Type = "NAME",
                        Name = _allegroSettings.AllegroResponsibleProducer
                    },
                    SafetyInformation = new SafetyInformation
                    {
                        Type = "TEXT",
                        Description = _allegroSettings.AllegroSafetyMeasures
                    }
                }
            };
        }

        private static TaxSettings BuildTaxSettings() => new()
        {
            Rates = new List<Rate> { new() { RateValue = "23.00", CountryCode = "PL" } },
            Subject = "GOODS"
        };

        private AfterSalesServices BuildAfterSalesServices() => new()
        {
            Warranty = new Warranty { Name = _allegroSettings.AllegroWarranty },
            ReturnPolicy = new ReturnPolicy { Name = _allegroSettings.AllegroReturnPolicy },
            ImpliedWarranty = new ImpliedWarranty { Name = _allegroSettings.AllegroImpliedWarranty }
        };

        // ---------------------------------------------------------------- tytuł oferty

        /// <summary>
        /// Tytuł oferty. Inter Cars nie podaje nazwy produktu, tylko nazwę grupy asortymentowej
        /// ("Filtr oleju"), więc najpierw sięgamy po nazwę produktu z katalogu Allegro - to ona
        /// opisuje konkretną część. Gdy produktu nie dopasowaliśmy, składamy tytuł z nazwy grupy,
        /// marki i numeru katalogowego; bez nich oferty jednej kategorii byłyby nie do odróżnienia.
        /// </summary>
        private static string BuildOfferName(RolmarProduct product)
        {
            var catalogName = Trim(ShortenLongWords((product.AllegroName ?? string.Empty).Trim()));

            // Nazwa katalogowa bywa krotka ("Kula John Deere L200878" to juz granica) - jesli nie
            // spelnia wymogow Allegro, skladamy tytul jak dla produktu bez dopasowania.
            if (IsOfferNameValid(catalogName))
                return catalogName;

            var parts = new List<string> { (product.Name ?? string.Empty).Trim() };

            AppendIfFits(parts, product.SupplierName);
            AppendIfFits(parts, product.CustomerCode);

            var name = ShortenLongWords(string.Join(' ', parts.Where(p => p.Length > 0)));

            if (!IsOfferNameValid(name))
            {
                var code = (product.Code ?? string.Empty).Trim();

                if (code.Length > 0 && !name.Contains(code, StringComparison.OrdinalIgnoreCase))
                    name = (name + " " + code).Trim();
            }

            return Trim(name);
        }

        private static string Trim(string name) =>
            name.Length > MaxOfferNameLength ? name[..MaxOfferNameLength].TrimEnd() : name;

        private static void AppendIfFits(List<string> parts, string? value)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Length == 0)
                return;

            if (parts.Any(p => p.Contains(text, StringComparison.OrdinalIgnoreCase)))
                return;

            if (string.Join(' ', parts).Length + text.Length + 1 > MaxOfferNameLength)
                return;

            parts.Add(text);
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
                    words[i] = words[i][..ShortenedWordLength];
            }

            return string.Join(' ', words);
        }

        private static bool IsOfferNameValid(string name) =>
            name.Length >= MinOfferNameLength &&
            name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= MinOfferNameWords;

        // ---------------------------------------------------------------- parametry i opis

        /// <summary>
        /// Parametry samej oferty. Parametrów opisujących produkt nie budujemy wcale - produkt
        /// bierzemy z katalogu Allegro, a on ma własne.
        /// </summary>
        private static List<Parameter> BuildOfferParameters(ICollection<ProductParameter> parameters)
        {
            var multiValueParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "numery katalogowe zamienników", "marka"
            };

            var result = new List<Parameter>();

            foreach (var param in parameters.Where(p => !p.IsForProduct))
            {
                if (string.IsNullOrWhiteSpace(param.Value))
                    continue;

                // Znaki sterujące Allegro odrzuca razem z całą ofertą.
                var cleaned = new string(param.Value.Where(ch => !char.IsControl(ch) || ch == ' ').ToArray()).Trim();

                var values = multiValueParams.Contains(param.Name)
                    ? cleaned
                        .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim())
                        .Where(v => v.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Take(15)
                        .ToList()
                    : new List<string> { cleaned };

                if (values.Count > 0)
                    result.Add(new Parameter { Name = param.Name, Values = values });
            }

            return result;
        }

        /// <summary>
        /// Opis oferty. Zdjęcia wstawiamy tylko wtedy, gdy je mamy - przy produktach Inter Cars
        /// opis jest czysto tekstowy, a galerię pokazuje Allegro z podpiętego produktu.
        /// </summary>
        private static Description BuildDescription(RolmarProduct product)
        {
            var description = new Description();

            var content = new StringBuilder()
                .Append($"<p><b>{Encode(product.Name)}</b></p>")
                .Append(Paragraph("Kod produktu", product.Code))
                .Append(Paragraph("Indeks", product.CustomerCode))
                .Append(Paragraph("Producent", product.SupplierName))
                .Append(Paragraph("Numer katalogowy", product.Substitutes))
                .Append(product.Description != null && product.Description != product.Name
                    ? Paragraph("Opis", product.Description)
                    : string.Empty)
                .ToString();

            description.Sections.Add(new Section
            {
                SectionItems = new List<SectionItem> { new() { Type = "TEXT", Content = content } }
            });

            var specifications = BuildSpecificationsHtml(product);

            if (specifications.Length > 0)
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem> { new() { Type = "TEXT", Content = specifications } }
                });
            }

            return description;
        }

        private static string BuildSpecificationsHtml(RolmarProduct product)
        {
            if (product.Specifications == null || product.Specifications.Count == 0)
                return string.Empty;

            var items = string.Join(string.Empty, product.Specifications
                .Where(s => !string.IsNullOrWhiteSpace(s.Name) && !string.IsNullOrWhiteSpace(s.Value))
                .Select(s => $"<li><b>{Encode(s.Name)}</b>: {Encode(s.Value)} {Encode(s.UnitName)}</li>"));

            return items.Length == 0 ? string.Empty : $"<p><b>Wymiary opakowania:</b></p><ul>{items}</ul>";
        }

        private static string Paragraph(string label, string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : $"<p><b>{label}: </b>{Encode(value)}</p>";

        /// <summary>Znaki sterujące psują walidację opisu po stronie Allegro - usuwamy je razem z kodowaniem HTML.</summary>
        private static string Encode(string? value) =>
            string.IsNullOrEmpty(value)
                ? string.Empty
                : new string(System.Net.WebUtility.HtmlEncode(value).Where(c => c >= 32 || c == 10 || c == 13).ToArray());

        // ---------------------------------------------------------------- cena i dostawa

        private static string MapAllegroUnit(string productUnit)
        {
            if (string.IsNullOrWhiteSpace(productUnit))
                return "UNIT";

            return productUnit.Trim().ToLowerInvariant().Replace(".", string.Empty) switch
            {
                "szt" => "UNIT",
                "para" => "PAIR",
                "kpl" => "SET",
                _ => "UNIT"
            };
        }

        public static decimal CalculatePrice(RolmarProduct product, PriceSettings priceSettings, List<DeliverySettings> deliveries)
        {
            // Marża własna z przedziału cenowego, a na niej prowizje Allegro - tak jak u pozostałych dostawców.
            var calculatedPrice = product.PriceGross * (1 + ResolveMargin(priceSettings, product.PriceGross) / 100m);

            return OfferPricing.Finalize(
                calculatedPrice,
                priceSettings,
                ProductDimensions.IsOversized(product, priceSettings.OversizeThresholdCm),
                chargeShipping: DeliveryMatcher.MatchDelivery(product, deliveries)?.IsSmart == true);
        }

        private static decimal ResolveMargin(PriceSettings priceSettings, decimal grossPrice)
        {
            if (priceSettings.MarginRanges.Count == 0)
                return 0m;

            return priceSettings.MarginRanges
                .FirstOrDefault(r => grossPrice >= r.Min && grossPrice <= r.Max)?.Margin
                ?? priceSettings.MarginRanges[^1].Margin;
        }

    }
}
