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

        public decimal CalculatePrice(RolmarProduct product) => CalculatePrice(product, _priceSettings);

        public ProductOfferRequest BuildOffer(RolmarProduct product)
        {
            var images = GalleryImages(product);

            return new ProductOfferRequest
            {
                Name = BuildOfferName(product),
                ProductSet = BuildProductSet(product, images),
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
                        Amount = CalculatePrice(product, _priceSettings).ToString("F2", CultureInfo.InvariantCulture),
                        Currency = "PLN"
                    }
                },
                Images = images,
                Description = BuildDescription(product, images),
                External = new External { Id = product.Code },
                Publication = new Publication
                {
                    Status = "ACTIVE",
                    StartingAt = DateTime.UtcNow
                },
                Delivery = new Delivery
                {
                    ShippingRates = new ShippingRates { Name = GetDelivery(product, _appSettings.Deliveries) },
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
                Parameters = BuildParameters(product.Parameters, isForProduct: false)
            };
        }

        public ProductOfferRequest PatchOffer(AllegroOffer offer, bool keepCurrentPrice)
        {
            // Oferta bez wczytanego produktu nie ma jak zostać zaktualizowana - lepiej głośny błąd niż cicha awaria.
            var product = offer.Product
                ?? throw new InvalidOperationException($"Oferta {offer.Id} nie ma wczytanego produktu.");

            // Oferta z cennika zarządzanego ręcznie: nie ruszamy ceny, cennika dostawy ani czasu realizacji.
            var manuallyManagedDelivery = PriceHelper.IsManuallyManagedDelivery(offer.DeliveryName, _appSettings.DeliveriesWithoutPriceUpdate);

            var connectedImages = product.AllegroImages?
                .Where(i => i.Connected)
                .Select(i => i.Url)
                .Distinct()
                .Take(MaxGalleryImages)
                .ToList();

            // Pusta lista zdjęć to u Inter Cars norma - wtedy pomijamy galerię i opis,
            // żeby patch nie skasował tego, co Allegro bierze z produktu w katalogu.
            var images = connectedImages is { Count: > 0 } ? connectedImages : null;

            return new ProductOfferRequest
            {
                // Bez productSet Allegro odrzuca patch: nie widzi podpietego produktu,
                // producenta odpowiedzialnego ani informacji o bezpieczenstwie (GPSR).
                ProductSet = BuildProductSet(product, images, offer.ProductId),
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
                            Amount = CalculatePrice(product, _priceSettings).ToString("F2", CultureInfo.InvariantCulture),
                            Currency = "PLN"
                        }
                    },
                Images = images,
                Description = images != null ? BuildDescription(product, images) : null,
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
                        ShippingRates = new ShippingRates { Name = GetDelivery(product, _appSettings.Deliveries) },
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
        /// Produkt z katalogu Allegro: gdy znamy jego id, wysyłamy wyłącznie id. Dołożenie zdjęć
        /// czy parametrów obok id Allegro traktuje jako propozycję zmiany produktu i wymaga wtedy
        /// kompletu parametrów - stąd "Uzupełnij parametry obowiązkowe" mimo podpiętego produktu.
        /// </summary>
        private List<ProductSet> BuildProductSet(RolmarProduct product, List<string>? images, string? offerProductId = null)
        {
            // Id produktu z katalogu: najpierw to, które oferta już ma, potem zapamiętane przy produkcie.
            var catalogProductId = string.IsNullOrWhiteSpace(offerProductId) ? product.AllegroId : offerProductId;
            var hasCatalogProduct = !string.IsNullOrWhiteSpace(catalogProductId);

            var allegroProduct = hasCatalogProduct
                ? new ProductObject { Id = catalogProductId }
                : new ProductObject
                {
                    Name = BuildOfferName(product),
                    Category = new Category { Id = product.DefaultAllegroCategory.ToString(CultureInfo.InvariantCulture) },
                    // Parametry opisujące produkt idą tutaj; parametry samej oferty - w sekcji parameters.
                    Parameters = BuildParameters(product.Parameters, isForProduct: true),
                    Images = images
                };

            return new List<ProductSet>
            {
                new()
                {
                    ProductObject = allegroProduct,
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

        private static List<Parameter> BuildParameters(ICollection<ProductParameter> parameters, bool isForProduct)
        {
            var multiValueParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "numery katalogowe zamienników", "marka"
            };

            var result = new List<Parameter>();

            foreach (var param in parameters.Where(p => p.IsForProduct == isForProduct))
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
        private static Description BuildDescription(RolmarProduct product, List<string>? images)
        {
            var description = new Description();
            var gallery = images ?? new List<string>();
            var imageIndex = 0;

            if (imageIndex < gallery.Count)
            {
                description.Sections.Add(new Section
                {
                    SectionItems = new List<SectionItem>
                    {
                        new() { Type = "IMAGE", Url = gallery[imageIndex++] }
                    }
                });
            }

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

            var mainSection = new List<SectionItem> { new() { Type = "TEXT", Content = content } };

            if (imageIndex < gallery.Count)
                mainSection.Add(new SectionItem { Type = "IMAGE", Url = gallery[imageIndex++] });

            description.Sections.Add(new Section { SectionItems = mainSection });

            var specifications = BuildSpecificationsHtml(product);

            if (specifications.Length > 0)
            {
                var items = new List<SectionItem>();

                if (imageIndex < gallery.Count)
                    items.Add(new SectionItem { Type = "IMAGE", Url = gallery[imageIndex++] });

                items.Add(new SectionItem { Type = "TEXT", Content = specifications });
                description.Sections.Add(new Section { SectionItems = items });
            }

            while (imageIndex < gallery.Count)
            {
                var items = new List<SectionItem> { new() { Type = "IMAGE", Url = gallery[imageIndex++] } };

                if (imageIndex < gallery.Count)
                    items.Add(new SectionItem { Type = "IMAGE", Url = gallery[imageIndex++] });

                description.Sections.Add(new Section { SectionItems = items });
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

        public static decimal CalculatePrice(RolmarProduct product, PriceSettings priceSettings)
        {
            // Marża własna z przedziału cenowego, a na niej prowizje Allegro - tak jak u pozostałych dostawców.
            var calculatedPrice = product.PriceGross * (1 + ResolveMargin(priceSettings, product.PriceGross) / 100m);

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

            // Produkt ponadgabarytowy - wysyłka droższa, dopłata idzie do finalnej ceny oferty.
            if (priceSettings.OversizeSurcharge > 0 && IsOversized(product, priceSettings))
                calculatedPrice += priceSettings.OversizeSurcharge;

            return Math.Max(calculatedPrice, 1.00m);
        }

        private static bool IsOversized(RolmarProduct product, PriceSettings priceSettings)
        {
            IReadOnlyCollection<string> names = priceSettings.OversizeDimensionNames.Count > 0
                ? priceSettings.OversizeDimensionNames
                : ProductDimensions.DefaultDimensionNames;

            return ProductDimensions.IsOversized(product, priceSettings.OversizeThresholdCm, names);
        }

        private static decimal ResolveMargin(PriceSettings priceSettings, decimal grossPrice)
        {
            if (priceSettings.MarginRanges.Count == 0)
                return 0m;

            return priceSettings.MarginRanges
                .FirstOrDefault(r => grossPrice >= r.Min && grossPrice <= r.Max)?.Margin
                ?? priceSettings.MarginRanges[^1].Margin;
        }

        /// <summary>
        /// Cennik dostawy dobrany do wagi i wymiarów opakowania podanych przez Inter Cars.
        /// Gdy nic nie pasuje, bierzemy największy - lepiej dopłacić do wysyłki niż nie wysłać.
        /// </summary>
        private static string? GetDelivery(RolmarProduct product, List<DeliverySettings> deliveries)
        {
            if (deliveries == null || deliveries.Count == 0)
                return null;

            var weight = (decimal)product.Weight;
            var length = GetDimensionCm(product, "Długość");
            var width = GetDimensionCm(product, "Szerokość");
            var height = GetDimensionCm(product, "Wysokość");

            // Inter Cars nie zawsze podaje gabaryty opakowania. Bez nich zerowa waga trafiłaby
            // w najtańszy cennik - a nieznany rozmiar to częściej duża paczka niż mała.
            if (weight <= 0 && length == null && width == null && height == null)
                return LargestDelivery(deliveries);

            var matching = deliveries
                .Where(d => d.Weight >= weight
                            && (length == null || d.Length >= length)
                            && (width == null || d.Width >= width)
                            && (height == null || d.Height >= height))
                .OrderBy(d => d.Weight)
                .ThenBy(d => d.Length * d.Width * d.Height)
                .FirstOrDefault();

            return matching?.DeliveryName ?? LargestDelivery(deliveries);
        }

        private static string? LargestDelivery(List<DeliverySettings> deliveries) => deliveries
            .OrderByDescending(d => d.Weight)
            .ThenByDescending(d => d.Length * d.Width * d.Height)
            .First()
            .DeliveryName;

        private static decimal? GetDimensionCm(RolmarProduct product, string dimensionName)
        {
            var spec = product.Specifications?
                .FirstOrDefault(s => string.Equals(s.Name, dimensionName, StringComparison.OrdinalIgnoreCase));

            if (spec == null || !decimal.TryParse(spec.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                return null;

            return spec.UnitName?.ToLowerInvariant() switch
            {
                "mm" => value / 10m,
                "cm" => value,
                "m" => value * 100m,
                _ => null
            };
        }
    }
}
