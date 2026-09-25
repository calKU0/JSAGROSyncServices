using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
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

        private readonly ILogger<AllegroProductService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IProductRepository _productRepository;
        private readonly ServiceContext _service;

        public AllegroProductService(
            ILogger<AllegroProductService> logger,
            AllegroApiClient apiClient,
            IProductRepository productRepository,
            ServiceContext serviceContext)
        {
            _logger = logger;
            _apiClient = apiClient;
            _productRepository = productRepository;
            _service = serviceContext;
        }

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

        public async Task<CatalogProductMatch> FindCatalogProduct(RolmarProduct product, CancellationToken ct)
        {
            var identifiers = ProductIdentifiers(product);

            // Bez własnego numeru katalogowego nie ma czym potwierdzić trafienia, a podpięcie
            // przypadkowego produktu psuje ofertę bardziej niż jego brak.
            if (identifiers.Count == 0)
                return CatalogProductMatch.None;

            // EAN jest jednoznaczny, więc pytamy o niego trybem GTIN - bez dopasowań przybliżonych.
            if (IsGtin(product.Ean))
            {
                var byEan = await SearchAsync(product.Ean!, gtin: true, identifiers, product, ct);

                if (byEan.ProductId != null)
                    return byEan;
            }

            foreach (var phrase in SearchPhrases(product))
            {
                var match = await SearchAsync(phrase, gtin: false, identifiers, product, ct);

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

            if (!string.IsNullOrWhiteSpace(product.Code))
                yield return product.Code;

            if (!string.IsNullOrWhiteSpace(product.Name))
                yield return product.Name;
        }

        private async Task<CatalogProductMatch> SearchAsync(
            string phrase,
            bool gtin,
            IReadOnlySet<string> identifiers,
            RolmarProduct product,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return CatalogProductMatch.None;

            var url = $"/sale/products?phrase={Uri.EscapeDataString(phrase)}" + (gtin ? "&mode=GTIN" : string.Empty);
            var result = await _apiClient.GetAsync<SearchProdustsResponse>(url, ct);

            var candidates = (result?.Products ?? []).Take(MaxCandidates)
                .Where(c => c.Id != null && c.Category?.Id != null)
                .ToList();

            // Trafienie w "Numer katalogowy części" jest pewniejsze niż w numer zamiennika czy
            // oryginału, więc najpierw szukamy dopasowania po numerze własnym produktu.
            var best = candidates.FirstOrDefault(c => Matches(c, identifiers, primaryOnly: true))
                ?? candidates.FirstOrDefault(c => Matches(c, identifiers, primaryOnly: false));

            if (best != null)
                return new CatalogProductMatch(best.Id, best.Category!.Id, best.Name);

            if (result?.Products.Count > 0)
            {
                _logger.LogDebug(
                    "Allegro returned {Count} products for '{Phrase}' ({Code}), none with a matching catalog number.",
                    result.Products.Count, phrase, product.Code);
            }

            return CatalogProductMatch.None;
        }

        /// <summary>
        /// Czy produkt z katalogu ma któryś z naszych numerów. Porównujemy po uproszczeniu zapisu,
        /// bo ten sam numer bywa pisany na kilka sposobów ("GE 60 ES-2RS /SKF/" i "GE60ES2RS-SKF").
        /// </summary>
        private static bool Matches(SearchProdustsResponse.Product candidate, IReadOnlySet<string> identifiers, bool primaryOnly)
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
            else
            {
                values.Add(product.Code);
                values.AddRange((product.Substitutes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries));
            }

            return values
                .Select(Normalize)
                .Where(value => value.Length >= MinIdentifierLength)
                .ToHashSet(StringComparer.Ordinal);
        }

        /// <summary>Krótsze ciągi to zwykle symbole typu "L" czy "24" - pasowałyby do wszystkiego.</summary>
        private const int MinIdentifierLength = 4;

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
        private static string Normalize(string? value)
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
