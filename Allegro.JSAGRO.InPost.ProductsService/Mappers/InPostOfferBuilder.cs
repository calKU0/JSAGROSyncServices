using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace Allegro.JSAGRO.InPost.ProductsService.Mappers
{
    /// <summary>
    /// Buduje ofertę InPost z oferty Allegro i produktu dostawcy. Wszystkie reguły "co, skąd
    /// i kiedy pominąć" są tutaj, bez wywołań API i bazy - żeby dało się je przetestować
    /// i zobaczyć w trybie próbnym dokładnie to, co poszłoby do InPost.
    /// </summary>
    public class InPostOfferBuilder
    {
        private const int MaxNameLength = 150;
        private const int MaxBrandLength = 100;
        private const int MaxSkuLength = 100;
        private const int MaxAttributeValueLength = 1024;
        private const int MaxAttributes = 120;
        private const int MaxMultiValues = 50;

        /// <summary>Wartości parametrów, które nic nie mówią - traktujemy je jak brak wartości.</summary>
        private static readonly HashSet<string> MeaninglessValues = new(StringComparer.OrdinalIgnoreCase)
        {
            "nieznany producent", "nieznany", "inna", "inny", "brak", "-", "n/a"
        };

        private static readonly Regex Digits = new(@"\D", RegexOptions.Compiled);
        private static readonly Regex Number = new(@"-?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
        private static readonly Regex NotAlphanumeric = new(@"[^a-z0-9]+", RegexOptions.Compiled);

        private readonly AppSettings _appSettings;
        private readonly InPostPriceSettings _priceSettings;
        private readonly InPostOfferSettings _offerSettings;
        private readonly IReadOnlyDictionary<string, AllegroResponsibleProducer> _producers;
        private readonly IReadOnlyDictionary<string, AllegroResponsiblePerson> _persons;

        public InPostOfferBuilder(
            AppSettings appSettings,
            InPostPriceSettings priceSettings,
            InPostOfferSettings offerSettings,
            IEnumerable<AllegroResponsibleProducer> producers,
            IEnumerable<AllegroResponsiblePerson> persons)
        {
            _appSettings = appSettings;
            _priceSettings = priceSettings;
            _offerSettings = offerSettings;

            _producers = producers
                .Where(p => !string.IsNullOrWhiteSpace(p.AllegroId))
                .GroupBy(p => p.AllegroId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            _persons = persons
                .Where(p => !string.IsNullOrWhiteSpace(p.AllegroId))
                .GroupBy(p => p.AllegroId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }

        public OfferBuildResult Build(InPostSourceOffer offer, string inPostCategoryId, IReadOnlyList<AttributeDefinition> attributeDefinitions)
        {
            var result = new OfferBuildResult { Offer = offer, InPostCategoryId = inPostCategoryId };
            var product = offer.Product;

            if (product.ArchivedAt != null)
                return result.Skip("Produkt wycofany u dostawcy.");

            var stock = (int)Math.Floor(product.InStock);

            if (stock < Math.Max(_appSettings.MinProductStock, 1))
                return result.Skip($"Stan u dostawcy {stock} poniżej minimum {_appSettings.MinProductStock}.");

            if (product.PriceGross <= 0)
                return result.Skip("Brak ceny zakupu.");

            var dimensionCheck = BuildDimension(offer, out var dimension);

            if (dimensionCheck != null)
                return result.Skip(dimensionCheck);

            var images = BuildImages(offer);

            if (images.Count == 0)
                return result.Skip("Brak zdjęć - InPost wymaga co najmniej jednego.");

            var name = Truncate(FirstNonEmpty(offer.Name, product.Name), MaxNameLength);

            if (string.IsNullOrWhiteSpace(name))
                return result.Skip("Brak nazwy.");

            var description = BuildDescription(offer);
            var brand = Truncate(ResolveBrand(offer), MaxBrandLength);

            if (string.IsNullOrWhiteSpace(brand))
                return result.Skip($"Nie udało się ustalić marki (ustaw DefaultBrand dla cennika '{offer.DeliveryName}').");

            var price = InPostPricing.Calculate(product, _priceSettings);
            var code = product.Code.Trim();

            var attributes = BuildAttributes(offer, brand, attributeDefinitions, result.Warnings);

            result.Price = price;
            result.Proposal = new OfferProposal
            {
                ExternalId = Truncate(code, 255),
                Product = new ProductInfoProposal
                {
                    Name = name,
                    Description = description,
                    Brand = brand,
                    CategoryId = inPostCategoryId,
                    Attributes = attributes.Count > 0 ? attributes : null,
                    Sku = Truncate(code, MaxSkuLength),
                    ManufacturerProductNumber = Truncate(code, 255),
                    Ean = NormalizeEan(product.Ean),
                    Dimension = dimension
                },
                Stock = new Stock
                {
                    Quantity = Math.Min(stock, 999_999),
                    Unit = MapUnit(product.Unit)
                },
                Price = new PriceTag
                {
                    GrossPrice = new Money { Amount = price.Final, Currency = "PLN" },
                    TaxRateInfo = $"{_priceSettings.VatRate}%"
                },
                Gpsr = _offerSettings.SendGpsr ? BuildGpsr(offer, result.Warnings) : null,
                ShippingTime = new ShippingTime { DaysToShip = DaysToShip(offer.HandlingTime) },
                Images = images,
                PostSale = BuildPostSale(),
                Features = new Features { Refundable = true }
            };

            if (result.Proposal.Product.Ean == null)
                result.Warnings.Add("Brak poprawnego EAN - InPost dopasuje produkt po marce i numerze producenta.");

            if (dimension?.Length == null)
                result.Warnings.Add("Brak pełnych wymiarów produktu - wysłana jest tylko waga.");
            else if (dimension.Source == DimensionSource.PriceList)
                result.Warnings.Add($"Brak wymiarów produktu - wysłane wymiary paczki z cennika '{offer.DeliveryName}'.");

            return result;
        }

        // ------------------------------------------------------------------ wymiary

        /// <summary>
        /// Wymiary (mm) i waga (g). Zwraca powód pominięcia, gdy produkt nie mieści się w paczce
        /// standardowej InPost - takiej oferty platforma i tak nie przyjmie.
        /// </summary>
        private string? BuildDimension(InPostSourceOffer offer, out Dimension? dimension)
        {
            dimension = null;

            var shippingClass = offer.Product.ShippingClass;

            if (ShippingClasses.IsFreight(shippingClass))
                return $"Towar paletowy / dłużyca wg dostawcy ('{shippingClass}') - nie mieści się w paczce InPost.";

            var sidesCm = ProductDimensions.ReadCm(offer.Product);
            var usedFallback = false;
            var usedShippingClass = false;

            // Znany bok produktu sprawdzamy przed podstawieniem paczki z cennika - inaczej tłumik
            // o długości 150 cm bez pozostałych wymiarów "zmieściłby się" w paczce zapasowej.
            if (sidesCm.Count > 0 && sidesCm[0] > _offerSettings.MaxSideCm)
                return $"Za duży na InPost: najdłuższy bok {sidesCm[0]:0.#} cm (limit {_offerSettings.MaxSideCm:0} cm).";

            // Kolejność źródeł wymiarów: specyfikacja produktu (komplet boków) → gabaryt paczkomatu
            // z klasy wysyłki dostawcy → największa paczka cennika dostawy.
            if (sidesCm.Count < 3 && ShippingClasses.ParcelSidesCm(shippingClass) is { } classSides
                && (sidesCm.Count == 0 || sidesCm[0] <= classSides[0]))
            {
                sidesCm = classSides;
                usedShippingClass = true;
            }
            else if (sidesCm.Count < 3 && FallbackSides(offer) is { } fallback)
            {
                // Znane boki produktu nie mogą zostać zaniżone przez paczkę z cennika - bierzemy większy.
                var known = sidesCm;
                sidesCm = fallback
                    .Select((side, i) => i < known.Count ? Math.Max(side, known[i]) : side)
                    .OrderByDescending(side => side)
                    .ToList();
                usedFallback = true;
            }

            var weightKg = offer.Product.Weight > 0 ? (decimal)offer.Product.Weight : offer.Weight;

            if (sidesCm.Count > 0 && sidesCm[0] > _offerSettings.MaxSideCm)
                return $"Za duży na InPost: najdłuższy bok {sidesCm[0]:0.#} cm (limit {_offerSettings.MaxSideCm:0} cm).";

            if (sidesCm.Count >= 3 && sidesCm.Take(3).Sum() > _offerSettings.MaxDimensionsSumCm)
                return $"Za duży na InPost: suma boków {sidesCm.Take(3).Sum():0.#} cm (limit {_offerSettings.MaxDimensionsSumCm:0} cm).";

            if (weightKg > _offerSettings.MaxWeightKg)
                return $"Za ciężki na InPost: {weightKg:0.##} kg (limit {_offerSettings.MaxWeightKg:0.##} kg).";

            var result = new Dimension();

            if (sidesCm.Count >= 3)
            {
                result.Length = ToMillimetres(sidesCm[0]);
                result.Width = ToMillimetres(sidesCm[1]);
                result.Height = ToMillimetres(sidesCm[2]);
            }

            if (weightKg > 0)
                result.Weight = Math.Max(1, (int)Math.Ceiling(weightKg * 1000m));

            if (result.Length != null || result.Weight != null)
                dimension = result;

            if (usedFallback)
                result.Source = DimensionSource.PriceList;
            else if (usedShippingClass)
                result.Source = DimensionSource.ShippingClass;
            else if (result.Length != null)
                result.Source = DimensionSource.Product;

            return null;
        }

        /// <summary>Wymiary zapasowe cennika oferty, od największego boku.</summary>
        private IReadOnlyList<decimal>? FallbackSides(InPostSourceOffer offer)
        {
            var list = _appSettings.DeliveryPriceLists
                .FirstOrDefault(l => string.Equals(l.Name, offer.DeliveryName, StringComparison.OrdinalIgnoreCase));

            if (list == null || list.FallbackLengthCm <= 0 || list.FallbackWidthCm <= 0 || list.FallbackHeightCm <= 0)
                return null;

            return new[] { list.FallbackLengthCm, list.FallbackWidthCm, list.FallbackHeightCm }
                .OrderByDescending(s => s)
                .ToList();
        }

        private static int ToMillimetres(decimal cm) => Math.Clamp((int)Math.Ceiling(cm * 10m), 1, 10_000);

        // ------------------------------------------------------------------ zdjęcia

        private List<OfferImage> BuildImages(InPostSourceOffer offer)
        {
            var urls = new List<string>();

            if (!string.IsNullOrWhiteSpace(offer.Images))
            {
                try
                {
                    urls.AddRange(JsonSerializer.Deserialize<List<string>>(offer.Images) ?? new List<string>());
                }
                catch (JsonException)
                {
                }
            }

            // Zdjęcia z opisu bywają spoza galerii (np. rysunek techniczny) - dokładamy je na końcu.
            urls.AddRange(offer.Descriptions
                .Where(d => string.Equals(d.Type, "IMAGE", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Content));

            var slug = Slug(offer.Product.Code);

            return urls
                .Where(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Clamp(_offerSettings.MaxImages, 1, 20))
                .Select((url, index) => new OfferImage
                {
                    FileName = $"{slug}-{index + 1}.jpg",
                    FileUrl = url,
                    Priority = index + 1
                })
                .ToList();
        }

        // ------------------------------------------------------------------ opis

        /// <summary>
        /// Opis z oferty Allegro (to, co widzi kupujący na Allegro: nazwa, kod, opis, parametry,
        /// zastosowanie, zamienniki), sekcja po sekcji. Zdjęcia z opisu idą do galerii.
        /// Gdy oferta nie ma opisu tekstowego, składamy go z danych produktu.
        /// </summary>
        private static string BuildDescription(InPostSourceOffer offer)
        {
            var html = string.Concat(offer.Descriptions
                .Where(d => string.Equals(d.Type, "TEXT", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(d.Content))
                .OrderBy(d => d.SectionId)
                .Select(d => d.Content.Trim()));

            if (!string.IsNullOrWhiteSpace(html))
                // Opisy ofert Allegro wystawionych przed poprawką mają tagi dostawcy zakodowane jako tekst
                // (&lt;b&gt;) - zamieniamy je na prawdziwe akapity, żeby kupujący w InPost nie widział tagów.
                return DescriptionFormatter.Format(SupplierHtml.ToBlocks(html, decodeEscapedTags: true));

            var product = offer.Product;
            var sb = new StringBuilder();

            sb.Append($"<p><b>{Encode(FirstNonEmpty(offer.Name, product.Name))}</b></p>");
            sb.Append($"<p><b>Kod produktu: </b>{Encode(product.Code)}</p>");

            if (!string.IsNullOrWhiteSpace(product.Description))
                sb.Append(SupplierHtml.ToLabelledBlocks("Opis:", product.Description));

            if (product.Specifications?.Count > 0)
            {
                sb.Append("<p><b>Parametry/Wymiary:</b></p><ul>");

                foreach (var s in product.Specifications)
                    sb.Append($"<li><b>{Encode(s.Name)}</b>: {Encode(s.Value)} {Encode(s.UnitName)}</li>");

                sb.Append("</ul>");
            }

            if (!string.IsNullOrWhiteSpace(product.Fits))
                sb.Append(SupplierHtml.ToLabelledBlocks("Pasuje do:", product.Fits));

            if (!string.IsNullOrWhiteSpace(product.Substitutes))
                sb.Append(SupplierHtml.ToLabelledBlocks("Symbol zamiennika:", product.Substitutes));

            return DescriptionFormatter.Format(sb.ToString());
        }

        // ------------------------------------------------------------------ marka

        /// <summary>
        /// Marka: reguła z konfiguracji pasująca do nazwy produktu, potem do ścieżki kategorii
        /// dostawcy (np. "WARYŃSKI ORIGIN>..."), potem parametr "Marka"/"Producent części",
        /// a na końcu marka domyślna.
        /// </summary>
        internal string ResolveBrand(InPostSourceOffer offer)
        {
            // Marka podana przez dostawcę ma pierwszeństwo; reguły ujednolicają jej zapis ("WARYNSKI" → "Waryński").
            var supplierBrand = offer.Product.SupplierName?.Trim();

            if (!string.IsNullOrEmpty(supplierBrand))
            {
                return _offerSettings.BrandRules
                    .FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Keyword) && Contains(supplierBrand, r.Keyword))?.Brand
                    ?? supplierBrand;
            }

            var texts = new[] { offer.Product.Name, offer.Name };

            foreach (var rule in _offerSettings.BrandRules.Where(r => !string.IsNullOrWhiteSpace(r.Keyword)))
            {
                if (texts.Any(t => Contains(t, rule.Keyword)))
                    return rule.Brand;
            }

            foreach (var rule in _offerSettings.BrandRules.Where(r => !string.IsNullOrWhiteSpace(r.Keyword)))
            {
                if (offer.SupplierCategoryPaths.Any(path => Contains(path, rule.Keyword)))
                    return rule.Brand;
            }

            var fromParameters = AllSourceValues(offer)
                .Where(v => Normalize(v.Name) is "marka" or "producent czesci" or "producent")
                .SelectMany(v => v.Values)
                .FirstOrDefault(IsMeaningful);

            return fromParameters ?? DefaultBrandFor(offer);
        }

        /// <summary>Marka domyślna cennika, z którego pochodzi oferta (Rolmar → "Rolmar", Gąska → "JAG").</summary>
        private string DefaultBrandFor(InPostSourceOffer offer) =>
            _appSettings.DeliveryPriceLists
                .FirstOrDefault(l => string.Equals(l.Name, offer.DeliveryName, StringComparison.OrdinalIgnoreCase))?
                .DefaultBrand ?? string.Empty;

        private static bool Contains(string? text, string keyword) =>
            !string.IsNullOrEmpty(text)
            && RemoveDiacritics(text).Contains(RemoveDiacritics(keyword), StringComparison.OrdinalIgnoreCase);

        // ------------------------------------------------------------------ atrybuty

        /// <summary>
        /// Atrybuty kategorii InPost wypełniane po nazwie. Nazwy atrybutów InPost pokrywają się
        /// z parametrami Allegro ("Numer katalogowy części", "Producent części"...), więc szukamy
        /// wartości kolejno w: parametrach oferty Allegro, parametrach produktu, specyfikacji
        /// produktu i na końcu w danych, które znamy na pewno (kod, zamienniki, marka).
        /// </summary>
        internal List<AttributeValue> BuildAttributes(
            InPostSourceOffer offer,
            string brand,
            IReadOnlyList<AttributeDefinition> definitions,
            List<string> warnings)
        {
            var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            void AddSource(string name, IEnumerable<string> values)
            {
                var key = Normalize(name);
                var cleaned = values.Where(IsMeaningful).Select(v => v.Trim()).ToList();

                if (key.Length == 0 || cleaned.Count == 0 || sources.ContainsKey(key))
                    return;

                sources[key] = cleaned;
            }

            foreach (var source in AllSourceValues(offer))
                AddSource(source.Name, source.Values);

            var product = offer.Product;

            AddSource("Numer katalogowy części", new[] { product.Code });
            AddSource("Kod producenta", new[] { product.Code });
            AddSource("Numer katalogowy producenta", new[] { product.Code });
            AddSource("Producent części", new[] { brand });
            AddSource("Marka", new[] { brand });

            if (!string.IsNullOrWhiteSpace(product.Substitutes))
                AddSource("Numery katalogowe zamienników", SplitList(product.Substitutes));

            // Jakość wg GVO tak jak u dostawcy, a gdy jej nie podał - zamiennik porównywalny z oryginałem.
            if (!string.IsNullOrWhiteSpace(_offerSettings.DefaultPartQuality))
                AddSource("Jakość części (zgodnie z GVO)", new[] { _offerSettings.DefaultPartQuality });

            var result = new List<AttributeValue>();

            foreach (var definition in definitions)
            {
                if (result.Count >= MaxAttributes)
                    break;

                var required = definition.ExpectedValue is "ONE" or "AT_LEAST_ONE";

                if (!sources.TryGetValue(Normalize(definition.Name), out var rawValues))
                {
                    if (required)
                        warnings.Add($"Brak wartości wymaganego atrybutu '{definition.Name}'.");

                    continue;
                }

                var values = ConvertValues(definition, rawValues);

                if (values.Count == 0)
                {
                    warnings.Add($"Wartość atrybutu '{definition.Name}' ({string.Join(", ", rawValues)}) nie pasuje do typu {definition.Type}.");
                    continue;
                }

                var single = definition.ExpectedValue is "ONE" or "NULL_OR_ONE";

                result.Add(new AttributeValue
                {
                    Id = definition.Id,
                    Lang = definition.Lang,
                    Values = single ? values.Take(1).ToList() : values.Take(MaxMultiValues).ToList()
                });
            }

            return result;
        }

        /// <summary>Parametry oferty, potem produktu, potem specyfikacje (z jednostką).</summary>
        private static IEnumerable<NamedValues> AllSourceValues(InPostSourceOffer offer)
        {
            foreach (var parameter in offer.OfferParameters)
                yield return parameter;

            foreach (var parameter in offer.Product.Parameters)
                yield return new NamedValues { Name = parameter.Name, Values = new List<string> { parameter.Value } };

            foreach (var specification in offer.Product.Specifications ?? new List<ProductSpecification>())
            {
                var value = string.IsNullOrWhiteSpace(specification.UnitName)
                    ? specification.Value
                    : $"{specification.Value} {specification.UnitName}".Trim();

                yield return new NamedValues { Name = specification.Name, Values = new List<string> { value } };
            }
        }

        private static List<string> ConvertValues(AttributeDefinition definition, List<string> rawValues)
        {
            IEnumerable<string?> converted = definition.Type switch
            {
                "NUMERIC" => rawValues.Select(v => ParseNumber(v) is { } n && n == Math.Truncate(n)
                    ? ((long)n).ToString(CultureInfo.InvariantCulture)
                    : null),
                "NUMERIC_FLOAT" => rawValues.Select(v => ParseNumber(v)?.ToString(CultureInfo.InvariantCulture)),
                "DATE" => rawValues.Select(v => DateTime.TryParse(v, CultureInfo.GetCultureInfo("pl-PL"), DateTimeStyles.None, out var d)
                    ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : null),
                "URL" => rawValues.Select(v => Uri.TryCreate(v, UriKind.Absolute, out _) ? v : null),
                "DICTIONARY" => rawValues.Select(v => definition.Dictionary?.Options
                    .FirstOrDefault(o => o.Active && string.Equals(o.Value, v, StringComparison.OrdinalIgnoreCase))?.Value),
                _ => rawValues.Select(v => Truncate(v, MaxAttributeValueLength))
            };

            return converted
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static decimal? ParseNumber(string value)
        {
            var match = Number.Match(value);

            return match.Success && decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
        }

        private static IEnumerable<string> SplitList(string value) =>
            value.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => v.Length > 0);

        // ------------------------------------------------------------------ GPSR, zwroty

        /// <summary>
        /// Producent i osoba odpowiedzialna z oferty Allegro. Sekcję wysyłamy tylko z kompletem
        /// wymaganych pól (nazwa i e-mail producenta) - niepełna sekcja GPSR blokuje ofertę.
        /// </summary>
        private GpsrInfo? BuildGpsr(InPostSourceOffer offer, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(offer.ResponsibleProducer)
                || !_producers.TryGetValue(offer.ResponsibleProducer, out var producer)
                || string.IsNullOrWhiteSpace(producer.Email))
            {
                warnings.Add("Brak pełnych danych producenta (GPSR) - sekcja GPSR pominięta.");
                return null;
            }

            GpsrResponsiblePerson? responsible = null;

            if (!string.IsNullOrWhiteSpace(offer.ResponsiblePerson) && _persons.TryGetValue(offer.ResponsiblePerson, out var person))
            {
                responsible = new GpsrResponsiblePerson
                {
                    Name = FirstNonEmpty(person.Name, person.PersonName),
                    Email = NullIfEmpty(person.Email),
                    Phone = NormalizePhone(person.Phone, person.CountryCode),
                    CountryCode = NullIfEmpty(person.CountryCode),
                    UnstructuredAddress = Address(person.Street, person.PostalCode, person.City)
                };
            }

            return new GpsrInfo
            {
                DoesNotRequireGpsrInfo = false,
                SafetyInformation = NullIfEmpty(_offerSettings.SafetyInformation),
                Manufacturer = new GpsrManufacturer
                {
                    Name = FirstNonEmpty(producer.TradeName, producer.Name),
                    Email = producer.Email!,
                    Phone = NormalizePhone(producer.Phone, producer.CountryCode),
                    CountryCode = NullIfEmpty(producer.CountryCode),
                    UnstructuredAddress = Address(producer.Street, producer.PostalCode, producer.City),
                    ResponsiblePersonDetails = responsible
                }
            };
        }

        private PostSale? BuildPostSale()
        {
            if (string.IsNullOrWhiteSpace(_offerSettings.ReturnPolicy) && string.IsNullOrWhiteSpace(_offerSettings.ComplaintPolicy))
                return null;

            return new PostSale
            {
                ReturnPolicy = string.IsNullOrWhiteSpace(_offerSettings.ReturnPolicy) ? null : new PolicyDescription { Description = _offerSettings.ReturnPolicy },
                ComplaintPolicy = string.IsNullOrWhiteSpace(_offerSettings.ComplaintPolicy) ? null : new PolicyDescription { Description = _offerSettings.ComplaintPolicy }
            };
        }

        /// <summary>Czas realizacji Allegro (np. "PT48H") w pełnych dniach.</summary>
        private int DaysToShip(string? handlingTime)
        {
            if (!string.IsNullOrWhiteSpace(handlingTime))
            {
                try
                {
                    var span = XmlConvert.ToTimeSpan(handlingTime);
                    return Math.Clamp((int)Math.Ceiling(span.TotalDays), 0, 60);
                }
                catch (FormatException)
                {
                }
            }

            return Math.Clamp(_offerSettings.DefaultDaysToShip, 0, 60);
        }

        // ------------------------------------------------------------------ drobne

        private static string MapUnit(string? unit) =>
            (unit ?? string.Empty).Trim().ToLowerInvariant().Replace(".", "") switch
            {
                "para" or "pary" => "PAIR",
                "kpl" or "komplet" => "SET",
                _ => "UNIT"
            };

        /// <summary>EAN to 8-14 cyfr. InPost nie pozwala go potem zmienić, więc wysyłamy tylko poprawny.</summary>
        internal static string? NormalizeEan(string? ean)
        {
            if (string.IsNullOrWhiteSpace(ean))
                return null;

            var digits = Digits.Replace(ean, string.Empty);

            return digits.Length is >= 8 and <= 14 ? digits : null;
        }

        /// <summary>InPost wymaga numeru z prefiksem kraju ("+48...").</summary>
        private static string? NormalizePhone(string? phone, string? countryCode)
        {
            if (string.IsNullOrWhiteSpace(phone))
                return null;

            var digits = Digits.Replace(phone, string.Empty);

            if (phone.TrimStart().StartsWith('+'))
                return "+" + digits;

            if (digits.Length == 9 && string.Equals(countryCode, "PL", StringComparison.OrdinalIgnoreCase))
                return "+48" + digits;

            return null;
        }

        private static string? Address(string? street, string? postalCode, string? city)
        {
            var cityLine = string.Join(' ', new[] { postalCode, city }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            var parts = new[] { street?.Trim(), cityLine }.Where(s => !string.IsNullOrWhiteSpace(s));
            var address = string.Join(", ", parts);

            return address.Length == 0 ? null : address;
        }

        private static bool IsMeaningful(string? value) =>
            !string.IsNullOrWhiteSpace(value) && !MeaninglessValues.Contains(value.Trim());

        internal static string Normalize(string? value) =>
            NotAlphanumeric.Replace(RemoveDiacritics(value ?? string.Empty).ToLowerInvariant(), " ").Trim();

        private static string RemoveDiacritics(string value)
        {
            var sb = new StringBuilder(value.Length);

            foreach (var c in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                sb.Append(c switch { 'ł' => 'l', 'Ł' => 'L', _ => c });
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string Slug(string value)
        {
            var slug = NotAlphanumeric.Replace(RemoveDiacritics(value).ToLowerInvariant(), "-").Trim('-');
            return slug.Length == 0 ? "produkt" : Truncate(slug, 80);
        }

        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;

        private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Truncate(string value, int max) =>
            value.Length <= max ? value : value[..max].TrimEnd();
    }

    public class OfferBuildResult
    {
        public InPostSourceOffer Offer { get; set; } = new();
        public string? InPostCategoryId { get; set; }
        public OfferProposal? Proposal { get; set; }
        public PriceBreakdown? Price { get; set; }
        public string? SkipReason { get; set; }
        public List<string> Warnings { get; } = new();

        public bool IsSkipped => SkipReason != null;

        /// <summary>
        /// Skrót treści oferty bez ceny i stanu (te mają osobne endpointy). Zmiana skrótu = coś w opisie,
        /// atrybutach, zdjęciach, wymiarach, GPSR albo zwrotach się zmieniło i ofertę trzeba zaktualizować.
        /// </summary>
        public string? ContentHash => Proposal == null ? null : ComputeContentHash(Proposal);

        internal static string ComputeContentHash(OfferProposal proposal)
        {
            var json = JsonSerializer.Serialize(new
            {
                proposal.Product,
                proposal.Gpsr,
                proposal.ShippingTime,
                proposal.Images,
                proposal.PostSale,
                proposal.Features
            }, Services.InPostClient.JsonOptions);

            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        public OfferBuildResult Skip(string reason)
        {
            SkipReason = reason;
            Proposal = null;
            return this;
        }
    }
}
