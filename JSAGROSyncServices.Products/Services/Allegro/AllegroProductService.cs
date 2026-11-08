using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    /// <summary>
    /// Dopasowanie produktów dostawcy do produktów z katalogu Allegro.
    ///
    /// Wyszukiwarka Allegro jest przybliżona - na dowolną frazę potrafi zwrócić "najbliższy"
    /// produkt, choćby nie miał z naszym nic wspólnego (kod "F7F6D8" zwracał zmieniarkę płyt
    /// o numerze "4H0035108F"). Dlatego bierzemy pod uwagę kilka pierwszych wyników i podpinamy
    /// tylko ten, którego numer katalogowy albo EAN faktycznie zgadza się z naszym.
    /// </summary>
    public class AllegroProductService : IAllegroProductService
    {
        /// <summary>Ile wyników wyszukiwania sprawdzać, zanim uznamy frazę za nietrafioną.</summary>
        private const int MaxCandidates = 10;

        /// <summary>Parametry Allegro z numerem katalogowym samego produktu - najmocniejsze potwierdzenie.</summary>
        private static readonly string[] PrimaryNumberParameters =
        [
            "Numer katalogowy części",
            "Kod producenta"
        ];

        /// <summary>Parametry z numerami powiązanymi (zamienniki, numer oryginału) - słabsze, ale wciąż wiarygodne.</summary>
        private static readonly string[] SecondaryNumberParameters =
        [
            "Numer katalogowy oryginału",
            "Numery katalogowe zamienników"
        ];

        private static readonly string[] EanParameters = ["EAN (GTIN)", "EAN", "GTIN"];

        /// <summary>Parametry Allegro z marką produktu - służą do potwierdzenia trafienia po samych cyfrach.</summary>
        private static readonly string[] BrandParameters =
        [
            "Marka",
            "Producent",
            "Producent części",
            "Marka części"
        ];

        private readonly ILogger<AllegroProductService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IProductRepository _productRepository;
        private readonly IOfferFactory _offerFactory;
        private readonly ServiceContext _service;

        public AllegroProductService(
            ILogger<AllegroProductService> logger,
            AllegroApiClient apiClient,
            IProductRepository productRepository,
            IOfferFactory offerFactory,
            ServiceContext serviceContext)
        {
            _logger = logger;
            _apiClient = apiClient;
            _productRepository = productRepository;
            _offerFactory = offerFactory;
            _service = serviceContext;
        }

        /// <summary>
        /// Czy produkt z katalogu musi mieć własne zdjęcia. Dotyczy dostawców, którzy nie wysyłają
        /// swojej galerii (Inter Cars): oferta bierze zdjęcia z produktu, więc podpięcie jej pod
        /// produkt bez zdjęć kończy się odrzuceniem - Allegro wymaga co najmniej jednego.
        /// </summary>
        private bool RequiresCatalogImages => !_offerFactory.UsesOwnImages;

        public async Task SearchProducts(CancellationToken ct = default)
        {
            var products = await _productRepository.GetNotExistingProductsInAllegro(ct);

            if (products.Count == 0)
            {
                _logger.LogInformation("Allegro product search: nothing to look up.");
                return;
            }

            _logger.LogInformation("Allegro product search: {Count} products to look up.", products.Count);

            int found = 0, notFound = 0, failed = 0;

            // Produkty, ktore odpytalismy - niezaleznie od wyniku. Nieznalezionych nie
            // odpytujemy ponownie w kazdym cyklu, tylko po uplywie okresu karencji.
            var searched = new ConcurrentBag<int>();
            var sw = Stopwatch.StartNew();

            await Parallel.ForEachAsync(
                products,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 5,
                    CancellationToken = ct
                },
                async (product, token) =>
                {
                    try
                    {
                        var allegroProduct = await FindCatalogProduct(product, token);

                        searched.Add(product.Id);

                        if (allegroProduct.ProductId == null || allegroProduct.CategoryId == null)
                        {
                            Interlocked.Increment(ref notFound);
                            _logger.LogDebug("Product not found on Allegro. EAN: {Ean}, Code: {Code}", product.Ean, product.Code);
                            return;
                        }

                        await _productRepository.UpdateProductAllegroId(product.Id, allegroProduct.ProductId, allegroProduct.CategoryId, allegroProduct.Name, token);

                        Interlocked.Increment(ref found);
                        _logger.LogDebug("Product {Code} matched Allegro product {AllegroId} in category {CategoryId}.", product.Code, allegroProduct.ProductId, allegroProduct.CategoryId);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogError(ex, "Searching Allegro for product {Code} failed.", product.Code);
                    }
                });

            sw.Stop();

            if (!searched.IsEmpty)
                await _productRepository.MarkAllegroSearched(searched, ct);

            _logger.LogInformation(
                "Allegro product search: matched {Found}, not found {NotFound}, failed {Failed} of {Total}. Took {Elapsed}.",
                found, notFound, failed, products.Count, sw.Elapsed);
        }

        public async Task<CatalogProductMatch> FindCatalogProduct(RolmarProduct product, CancellationToken ct,
            IReadOnlyCollection<string>? excludedIds = null, IReadOnlyCollection<string>? requiredParameters = null)
        {
            var mustHave = requiredParameters ?? [];

            var identifiers = ProductIdentifiers(product);

            // Bez własnego numeru katalogowego nie ma czym potwierdzić trafienia, a podpięcie
            // przypadkowego produktu psuje ofertę bardziej niż jego brak.
            if (identifiers.Count == 0)
                return CatalogProductMatch.None;

            // Produkty, które Allegro odłączyło od oferty ("Produkt nie pasuje do oferty") - nie podpinamy ich ponownie.
            // Do tego produkty wykluczone przez wołającego (np. bieżący, bez kompletu parametrów obowiązkowych).
            var rejected = await _productRepository.GetRejectedAllegroIds(product.Id, ct);

            foreach (var excluded in excludedIds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(excluded))
                    rejected.Add(excluded);
            }

            // EAN jest jednoznaczny, więc pytamy o niego trybem GTIN - bez dopasowań przybliżonych.
            if (IsGtin(product.Ean))
            {
                var byEan = await SearchAsync(product.Ean!, gtin: true, identifiers, product, rejected, mustHave, ct);

                if (byEan.ProductId != null)
                    return byEan;
            }

            foreach (var phrase in SearchPhrases(product))
            {
                var match = await SearchAsync(phrase, gtin: false, identifiers, product, rejected, mustHave, ct);

                if (match.ProductId != null)
                    return match;
            }

            return CatalogProductMatch.None;
        }

        /// <summary>
        /// Frazy do wyszukania, od najbardziej precyzyjnej. Inter Cars identyfikuje towar własnym
        /// SKU ("F7F6D8"), którego w katalogu Allegro nie ma - szukamy po numerze katalogowym
        /// producenta i indeksie. Gąska i Rolmar mają numer katalogowy wprost w kodzie produktu.
        /// </summary>
        private IEnumerable<string> SearchPhrases(RolmarProduct product)
        {
            if (_service.Company == IntegrationCompany.InterCars)
            {
                var brand = (product.SupplierName ?? string.Empty).Trim();
                var numbers = new[] { product.Substitutes, product.CustomerCode }
                    .SelectMany(CatalogNumberVariants)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var number in numbers)
                {
                    if (brand.Length > 0)
                        yield return $"{brand} {number}";

                    yield return number;
                }

                yield break;
            }

            if (_service.Company == IntegrationCompany.Rolmar)
            {
                // Jak przy Inter Cars: marka + numer, potem sam numer. Bez szukania po nazwie -
                // trafiała w przypadkowe produkty o podobnym opisie.
                var brand = (product.SupplierName ?? string.Empty).Trim();

                foreach (var number in RolmarNumberVariants(product.Code))
                {
                    if (brand.Length > 0)
                        yield return $"{brand} {number}";

                    yield return number;
                }

                yield break;
            }

            if (!string.IsNullOrWhiteSpace(product.Code))
                yield return product.Code;

            if (!string.IsNullOrWhiteSpace(product.Name))
                yield return product.Name;
        }

        /// <summary>Przedrostek grupy towarowej w indeksie Rolmaru ("W-5036020600").</summary>
        private static readonly Regex RolmarPrefix = new(@"^[A-Za-z]{1,2}-(?=[^-]*\d)", RegexOptions.Compiled);

        /// <summary>
        /// Indeks Rolmaru i jego wariant bez przedrostka grupy. Przedrostek ("W-") dopisuje Rolmar -
        /// numer producenta, który zna katalog Allegro, to reszta indeksu.
        /// </summary>
        internal static IEnumerable<string> RolmarNumberVariants(string? code)
        {
            var value = (code ?? string.Empty).Trim();

            if (value.Length == 0)
                yield break;

            yield return value;

            var withoutPrefix = RolmarPrefix.Replace(value, string.Empty).Trim();

            if (!string.Equals(withoutPrefix, value, StringComparison.Ordinal) && IsUsableIdentifier(Normalize(withoutPrefix)))
                yield return withoutPrefix;
        }

        private async Task<CatalogProductMatch> SearchAsync(
            string phrase,
            bool gtin,
            IReadOnlySet<string> identifiers,
            RolmarProduct product,
            IReadOnlySet<string> rejected,
            IReadOnlyCollection<string> mustHave,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return CatalogProductMatch.None;

            var url = $"/sale/products?phrase={Uri.EscapeDataString(phrase)}" + (gtin ? "&mode=GTIN" : string.Empty);
            var result = await _apiClient.GetAsync<SearchProdustsResponse>(url, ct);

            var candidates = (result?.Products ?? []).Take(MaxCandidates)
                .Where(c => c.Id != null && c.Category?.Id != null)
                .Where(c => !RequiresCatalogImages || c.Images.Count > 0)
                .Where(c => !rejected.Contains(c.Id!))
                .ToList();

            // Trafienie w "Numer katalogowy części" jest pewniejsze niż w numer zamiennika czy
            // oryginału, więc najpierw kandydaci dopasowani po numerze własnym produktu.
            var matched = candidates.Where(c => Matches(c, identifiers, primaryOnly: true) && Confirms(c, identifiers, product))
                .Concat(candidates.Where(c => Matches(c, identifiers, primaryOnly: false) && Confirms(c, identifiers, product)))
                .DistinctBy(c => c.Id)
                .ToList();

            foreach (var candidate in matched)
            {
                // Allegro pozwala na jedną aktywną ofertę danego produktu z katalogu. Produkt podpięty już pod
                // inny nasz produkt (także innego dostawcy - oba sprzedajemy na tych samych kontach) blokował
                // aktywację drugiej oferty: "Przedmiot ... znajduje się już na liście Twoich aktywnych ofert".
                if (await _productRepository.IsAllegroIdTakenByOther(candidate.Id!, product.Id, ct))
                {
                    _logger.LogDebug("Allegro product {ProductId} matches {Code} but is already linked to another product.", candidate.Id, product.Code);
                    continue;
                }

                // Parametry produktu bierzemy wyłącznie z katalogu, więc produkt bez kompletu
                // obowiązkowych kończyłby się "Uzupełnij parametry obowiązkowe". Bierzemy pierwszy
                // kompletny, niekompletne pomijamy.
                if (_service.ProductParametersFromCatalog)
                {
                    var missing = await MissingRequiredParameters(candidate, ct);
                    missing.AddRange(MissingNamedParameters(candidate, mustHave));

                    if (missing.Count > 0)
                    {
                        _logger.LogDebug("Allegro product {ProductId} matches {Code} but lacks required parameters: {Missing}.",
                            candidate.Id, product.Code, string.Join(", ", missing));
                        continue;
                    }
                }

                return new CatalogProductMatch(candidate.Id, candidate.Category!.Id, candidate.Name);
            }

            if (result?.Products.Count > 0)
            {
                _logger.LogDebug(
                    "Allegro returned {Count} products for '{Phrase}' ({Code}), none with a matching catalog number.",
                    result.Products.Count, phrase, product.Code);
            }

            return CatalogProductMatch.None;
        }

        /// <summary>Jak długo trzymamy listę obowiązkowych parametrów kategorii (zmienia się rzadko).</summary>
        private static readonly TimeSpan RequiredParametersCacheTime = TimeSpan.FromHours(12);

        /// <summary>Po nieudanym pobraniu pytamy o kategorię ponownie dopiero po tym czasie.</summary>
        private static readonly TimeSpan FailedRequiredParametersRetry = TimeSpan.FromMinutes(10);

        private static readonly ConcurrentDictionary<string, (DateTime ExpiresAt, IReadOnlyList<CategoryParameterItem>? Parameters)> CategoryParametersCache = new();

        /// <summary>
        /// Obowiązkowe parametry produktu w kategorii kandydata, których kandydat nie ma uzupełnionych.
        /// Gdy listy parametrów kategorii nie da się pobrać, kandydata nie odrzucamy - ewentualny brak
        /// zgłosi Allegro przy wystawianiu, a serwis ofert podmieni wtedy produkt.
        /// </summary>
        private async Task<List<string>> MissingRequiredParameters(SearchProdustsResponse.Product candidate, CancellationToken ct)
        {
            var categoryId = candidate.Category?.Id;

            if (string.IsNullOrWhiteSpace(categoryId))
                return [];

            var parameters = await GetCategoryParameters(categoryId, ct);

            return parameters == null ? [] : MissingRequiredParameters(candidate, parameters);
        }

        /// <summary>Te z podanych nazw parametrów, których produkt z katalogu nie ma uzupełnionych.</summary>
        internal static IEnumerable<string> MissingNamedParameters(SearchProdustsResponse.Product candidate, IEnumerable<string> names)
        {
            var filled = candidate.Parameters
                .Where(HasValue)
                .Select(p => p.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return names.Where(name => !string.IsNullOrWhiteSpace(name) && !filled.Contains(name.Trim()));
        }

        /// <summary>Nazwy obowiązkowych parametrów, których produkt z katalogu nie ma uzupełnionych.</summary>
        internal static List<string> MissingRequiredParameters(SearchProdustsResponse.Product candidate, IEnumerable<CategoryParameterItem> required)
        {
            var filled = candidate.Parameters
                .Where(HasValue)
                .SelectMany(p => new[] { p.Id, p.Name })
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return required
                .Where(IsRequiredForCatalogProduct)
                .Where(p => !filled.Contains(p.Id ?? string.Empty) && !filled.Contains(p.Name ?? string.Empty))
                .Select(p => p.Name ?? p.Id ?? "-")
                .ToList();
        }

        /// <summary>
        /// Parametr, który musi mieć produkt z katalogu: wymagany dla produktu albo wymagany
        /// i opisujący produkt (takiego nie da się podać w sekcji oferty).
        /// </summary>
        internal static bool IsRequiredForCatalogProduct(CategoryParameterItem parameter) =>
            parameter.RequiredForProduct || (parameter.Required && parameter.Options?.DescribesProduct == true);

        private static bool HasValue(SearchProdustsResponse.Parameter parameter) =>
            (parameter.Values?.Any(v => !string.IsNullOrWhiteSpace(v)) ?? false)
            || (parameter.ValuesIds?.Any(v => !string.IsNullOrWhiteSpace(v)) ?? false)
            || parameter.RangeValue != null;

        public async Task<IReadOnlyList<CategoryParameterItem>?> GetCategoryParameters(string categoryId, CancellationToken ct)
        {
            if (CategoryParametersCache.TryGetValue(categoryId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
                return cached.Parameters;

            IReadOnlyList<CategoryParameterItem>? parameters = null;

            try
            {
                var response = await _apiClient.GetAsync<CategoryParametersResponse>($"/sale/categories/{categoryId}/parameters", ct);

                parameters = response?.Parameters?.ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Fetching parameters of category {CategoryId} failed - candidates are not checked for required parameters.", categoryId);
            }

            CategoryParametersCache[categoryId] = (
                DateTime.UtcNow + (parameters == null ? FailedRequiredParametersRetry : RequiredParametersCacheTime),
                parameters);

            return parameters;
        }

        /// <summary>
        /// Czy produkt z katalogu ma któryś z naszych numerów. Porównujemy po uproszczeniu zapisu,
        /// bo ten sam numer bywa pisany na kilka sposobów ("GE 60 ES-2RS /SKF/" i "GE60ES2RS-SKF").
        /// </summary>
        internal static bool Matches(SearchProdustsResponse.Product candidate, IReadOnlySet<string> identifiers, bool primaryOnly)
        {
            foreach (var parameter in candidate.Parameters)
            {
                if (!IsIdentifyingParameter(parameter.Name, primaryOnly))
                    continue;

                foreach (var value in parameter.Values ?? [])
                {
                    if (identifiers.Contains(Normalize(value)))
                        return true;
                }
            }

            if (primaryOnly)
                return false;

            // Numer katalogowy bardzo często siedzi w samej nazwie produktu - wtedy parametry
            // bywają puste, a trafienie i tak jest pewne.
            foreach (var token in TokenizeName(candidate.Name))
            {
                if (identifiers.Contains(token))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Druga, niezależna przesłanka trafienia - wymagana zawsze, gdy numer, po którym
        /// rozpoznaliśmy produkt, nie jest dość charakterystyczny (<see cref="IsStrongIdentifier"/>).
        /// Takie numery nie są w katalogu Allegro unikalne: "11119001" to u nas tarcza hamulcowa SRP,
        /// a w katalogu "Kod producenta" koralików szklanych JABLONEX; "279-V" to pierścień SBP
        /// i zarazem lakier do włosów. W obu przypadkach oferta trafiała pod zupełnie inny produkt.
        /// </summary>
        internal static bool Confirms(SearchProdustsResponse.Product candidate, IReadOnlySet<string> identifiers, RolmarProduct product)
        {
            if (MatchesOnStrongIdentifier(candidate, identifiers))
                return true;

            var brand = Normalize(product.SupplierName);

            if (brand.Length > 0)
            {
                foreach (var parameter in candidate.Parameters)
                {
                    if (!BrandParameters.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    foreach (var value in parameter.Values ?? [])
                    {
                        if (Normalize(value) == brand)
                            return true;
                    }
                }

                // Nazwy produktów katalogowych dla części zwykle zawierają markę.
                if (Normalize(candidate.Name).Contains(brand, StringComparison.Ordinal))
                    return true;
            }

            // Druga możliwość: nazwy mówią o tym samym. "TULEJA ZAWIESZENIA VOLVO WOZIDŁO"
            // i nasze "Pozostałe elementy zawieszenia TUZ" dzielą "zawieszenia"; koraliki
            // szklane z tarczą hamulcową nie dzielą nic.
            return SharesMeaningfulWord(candidate.Name, product.Name);
        }

        /// <summary>Najkrótsze słowo, które coś znaczy. Krótsze ("do", "pod", "na") pasują do wszystkiego.</summary>
        private const int MinSharedWordLength = 6;

        /// <summary>
        /// Słowa dość długie, a mimo to nic nie mówiące o tym, czym jest towar. Nazwy kategorii
        /// Inter Cars składają się głównie z nich ("Pozostałe elementy zawieszenia"), więc bez tej
        /// listy "Zestaw do Volvo" potwierdzałby "Zestaw do CAT".
        /// </summary>
        private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
        {
            "ZESTAW", "ZESTAWY", "KOMPLET", "POZOSTAŁE", "POZOSTALE", "ELEMENT", "ELEMENTY",
            "CZĘŚCI", "CZESCI", "CZĘŚĆ", "CZESC", "AKCESORIA", "INNE", "UNIWERSALNY",
            "UNIWERSALNE", "ORYGINALNY", "ORYGINALNE", "PRODUKT", "TOWAR"
        };

        /// <summary>Czy obie nazwy mają wspólne słowo na tyle długie, żeby mówić o tym samym towarze.</summary>
        internal static bool SharesMeaningfulWord(string? first, string? second)
        {
            var words = MeaningfulWords(first);

            return words.Count > 0 && MeaningfulWords(second).Any(words.Contains);
        }

        private static HashSet<string> MeaningfulWords(string? name) =>
            (name ?? string.Empty)
                .Split([' ', ',', ';', '/', '\\', '(', ')', '-', '.'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(word => word.Length >= MinSharedWordLength
                               && word.All(char.IsLetter)
                               && !GenericWords.Contains(word))
                .ToHashSet(StringComparer.Ordinal);

        /// <summary>
        /// Najkrótszy numer, który sam w sobie identyfikuje towar. Krótsze trafiają w katalogu
        /// Allegro w produkty z zupełnie innych branż - "279-V" to u nas pierścień, a w katalogu
        /// lakier do włosów.
        /// </summary>
        private const int StrongIdentifierLength = 6;

        /// <summary>
        /// Czy numer jest dość charakterystyczny, żeby samo jego trafienie potwierdzało produkt:
        /// dość długi i z literą. Same cyfry odpadają niezależnie od długości, bo numeryczne kody
        /// producenta powtarzają się między branżami.
        /// </summary>
        internal static bool IsStrongIdentifier(string identifier) =>
            identifier.Length >= StrongIdentifierLength && identifier.Any(char.IsLetter);

        /// <summary>Czy trafienie opiera się na numerze dość charakterystycznym, by wystarczył sam.</summary>
        internal static bool MatchesOnStrongIdentifier(SearchProdustsResponse.Product candidate, IReadOnlySet<string> identifiers)
        {
            foreach (var parameter in candidate.Parameters)
            {
                if (!IsIdentifyingParameter(parameter.Name, primaryOnly: false))
                    continue;

                foreach (var value in parameter.Values ?? [])
                {
                    var normalized = Normalize(value);

                    if (identifiers.Contains(normalized) && IsStrongIdentifier(normalized))
                        return true;
                }
            }

            foreach (var token in TokenizeName(candidate.Name))
            {
                if (identifiers.Contains(token) && IsStrongIdentifier(token))
                    return true;
            }

            return false;
        }

        private static bool IsIdentifyingParameter(string? name, bool primaryOnly)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (PrimaryNumberParameters.Contains(name, StringComparer.OrdinalIgnoreCase)
                || EanParameters.Contains(name, StringComparer.OrdinalIgnoreCase))
                return true;

            return !primaryOnly && SecondaryNumberParameters.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Nasze identyfikatory: numer katalogowy producenta i EAN. Kod Inter Cars celowo pomijamy -
        /// to identyfikator wewnętrzny dostawcy, którego katalog Allegro nie zna.
        /// </summary>
        private IReadOnlySet<string> ProductIdentifiers(RolmarProduct product)
        {
            var values = new List<string?> { product.Ean };

            if (_service.Company == IntegrationCompany.InterCars)
            {
                values.AddRange(CatalogNumberVariants(product.Substitutes));
                values.AddRange(CatalogNumberVariants(product.CustomerCode));
            }
            else if (_service.Company == IntegrationCompany.Rolmar)
            {
                // Tylko numer samego produktu. Zamienniki Rolmaru to numery oryginałów - trafienie
                // w nie podpinało część innego producenta, czyli inny produkt.
                values.AddRange(RolmarNumberVariants(product.Code));
            }
            else
            {
                values.Add(product.Code);
                values.AddRange((product.Substitutes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
            }

            return values
                .Select(Normalize)
                .Where(IsUsableIdentifier)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Krótsze ciągi to zwykle symbole typu "L" czy "24" - pasowałyby do wszystkiego.</summary>
        private const int MinIdentifierLength = 4;

        /// <summary>
        /// Numer złożony z samych cyfr musi być dłuższy: czterocyfrowe "480" trafia w katalogu
        /// Allegro w dziesiątki niezwiązanych produktów, a jedno takie trafienie podmieniło
        /// przenośnik na zacisk hamulca. Numery z literami rozróżniają same z siebie.
        /// </summary>
        private const int MinNumericIdentifierLength = 6;

        /// <summary>Czy numer jest na tyle charakterystyczny, żeby nim szukać i potwierdzać trafienie.</summary>
        internal static bool IsUsableIdentifier(string identifier) =>
            identifier.Length >= MinIdentifierLength
            && (identifier.Length >= MinNumericIdentifierLength || !identifier.All(char.IsDigit));

        /// <summary>Końcówka "-JD", "-CNH", "-MF" - znacznik marki dopisywany przez Inter Cars.</summary>
        private static readonly Regex BrandSuffix = new(@"-[A-Za-z]{2,4}$", RegexOptions.Compiled);

        /// <summary>
        /// Numer katalogowy i jego wariant bez znacznika marki. Inter Cars dopisuje do numerów
        /// oryginalnych skrót producenta ("L166731-JD"), którego katalog Allegro nie zna -
        /// z sufiksem wyszukiwarka nie zwraca nic, bez niego trafia w numer oryginału.
        /// </summary>
        private static IEnumerable<string> CatalogNumberVariants(string? number)
        {
            var value = (number ?? string.Empty).Trim();

            if (value.Length == 0)
                yield break;

            yield return value;

            var withoutSuffix = BrandSuffix.Replace(value, string.Empty).Trim();

            if (withoutSuffix.Length >= MinIdentifierLength && !string.Equals(withoutSuffix, value, StringComparison.Ordinal))
                yield return withoutSuffix;
        }

        /// <summary>
        /// Czy wartość wygląda na kod kreskowy (EAN-8, UPC, EAN-13, GTIN-14). Inter Cars wpisuje
        /// w to pole także numery katalogowe - szukanie ich trybem GTIN to zmarnowane zapytanie.
        /// </summary>
        private static bool IsGtin(string? value)
        {
            var digits = Normalize(value);

            return digits.Length is 8 or 12 or 13 or 14 && digits.All(char.IsDigit);
        }

        /// <summary>Ile kolejnych słów nazwy sklejać, szukając w niej numeru katalogowego.</summary>
        private const int MaxNameTokenGroup = 4;

        /// <summary>
        /// Fragmenty nazwy produktu, które mogą być numerem katalogowym. Numery oryginalne bywają
        /// pisane ze spacjami ("1J0 612 041GD"), więc oprócz pojedynczych słów sprawdzamy też
        /// sklejenia sąsiednich. Sklejamy wyłącznie całe słowa - dzięki temu nasz "L166731" nie
        /// pasuje do cudzego "640L166731", czyli innego numeru.
        /// </summary>
        private static IEnumerable<string> TokenizeName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                yield break;

            var words = name
                .Split([' ', ',', ';', '/', '\\', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Where(word => word.Length > 0)
                .ToList();

            for (var start = 0; start < words.Count; start++)
            {
                var token = string.Empty;

                for (var length = 0; length < MaxNameTokenGroup && start + length < words.Count; length++)
                {
                    token += words[start + length];

                    if (token.Length >= MinIdentifierLength && token.Any(char.IsDigit))
                        yield return token;
                }
            }
        }

        /// <summary>Zostawia same litery i cyfry, wielkimi literami - spacje, kropki i myślniki nic nie znaczą.</summary>
        internal static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var builder = new StringBuilder(value.Length);

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                    builder.Append(char.ToUpperInvariant(character));
            }

            return builder.ToString();
        }
    }
}
