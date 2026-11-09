using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    /// <summary>
    /// Trzyma oferty produktu w jednej kategorii na wszystkich kontach i u wszystkich dostawców.
    ///
    /// Kategorię docelową wyznaczają wyłącznie dane wspólne dla kont: kategoria produktu z katalogu Allegro,
    /// a bez niego domyślna kategoria produktu. Oferty, które stały w różnych kategoriach (przeniesione ręcznie
    /// albo przez Allegro), żądały różnych parametrów obowiązkowych - konta podmieniały sobie przez to produkt
    /// z katalogu w kółko, a oferta w obcej kategorii (KUN1 w kategorii kawy) nie dawała się zaktualizować.
    ///
    /// Przy przeniesieniu wysyłamy pełną listę parametrów oferty ważną w kategorii docelowej: nasze wyliczone
    /// plus te z bieżącej oferty, które kategoria docelowa zna. Pusta lista nie czyści parametrów - Allegro
    /// zostawiało stare i odrzucało patch (InvalidParameterIdsInCategory). Odrzucone przeniesienie nie blokuje
    /// aktualizacji: oferta dostaje zwykły patch, a ta sama próba wraca dopiero po <see cref="RetryRejectedAfter"/>.
    /// </summary>
    public sealed class OfferCategoryAligner
    {
        /// <summary>Kategoria produktu z katalogu i to, czy kategoria jest liściem, zmieniają się rzadko.</summary>
        private static readonly TimeSpan CacheTime = TimeSpan.FromHours(24);

        /// <summary>
        /// Po odrzuceniu przez Allegro ponawiamy przeniesienie najwcześniej po tym czasie. Odrzucenia są zwykle
        /// trwałe - oferta trzyma po stronie Allegro parametry produktu ze starej kategorii (zapisane dawniej
        /// razem z id produktu), których API nie pozwala usunąć - więc nie ponawiamy ich częściej niż raz w tygodniu.
        /// </summary>
        public static readonly TimeSpan RetryRejectedAfter = TimeSpan.FromDays(7);

        private static readonly ConcurrentDictionary<string, (DateTime ExpiresAt, int? CategoryId)> CatalogCategories = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<int, (DateTime ExpiresAt, bool? Leaf)> LeafCategories = new();
        private static readonly ConcurrentDictionary<(string OfferId, int CategoryId), DateTime> RejectedMoves = new();

        private readonly AllegroApiClient _apiClient;
        private readonly IAllegroProductService _allegroProductService;
        private readonly IProductRepository _productRepository;
        private readonly ServiceContext _service;
        private readonly ILogger<OfferCategoryAligner> _logger;

        public OfferCategoryAligner(
            AllegroApiClient apiClient,
            IAllegroProductService allegroProductService,
            IProductRepository productRepository,
            ServiceContext serviceContext,
            ILogger<OfferCategoryAligner> logger)
        {
            _apiClient = apiClient;
            _allegroProductService = allegroProductService;
            _productRepository = productRepository;
            _service = serviceContext;
            _logger = logger;
        }

        /// <summary>
        /// Przeniesienie oferty do kategorii docelowej albo <c>null</c>, gdy oferta już w niej stoi, kategorii
        /// docelowej albo jej parametrów nie da się ustalić, albo Allegro odrzuciło niedawno to samo przeniesienie.
        /// Kompletność sprawdza <see cref="MissingIn"/> na zbudowanym patchu.
        /// </summary>
        public async Task<CategoryMove?> PlanAsync(AllegroOffer offer, string? catalogProductId, CancellationToken ct)
        {
            var product = offer.Product!;
            var target = await TargetCategoryAsync(product, catalogProductId, offer.CategoryId, ct);

            if (target == null || target == offer.CategoryId)
                return null;

            if (RejectedMoves.TryGetValue((offer.Id, target.Value), out var rejectedAt) && DateTime.UtcNow - rejectedAt < RetryRejectedAfter)
                return null;

            var categoryParameters = await _allegroProductService.GetCategoryParameters(target.Value.ToString(CultureInfo.InvariantCulture), ct);

            if (categoryParameters == null)
                return null;

            var retained = await RetainedOfferParametersAsync(offer.Id, categoryParameters, ct);

            if (retained == null)
                return null;

            return new CategoryMove(
                target.Value,
                product.AllParameters.Where(p => p.CategoryId == target.Value).ToList(),
                categoryParameters,
                retained,
                LinkedToCatalog: !string.IsNullOrWhiteSpace(catalogProductId) || _service.ProductParametersFromCatalog);
        }

        /// <summary>
        /// Przed wystawieniem oferty: ustawia domyślną kategorię produktu na kategorię jego produktu z katalogu.
        /// Zwraca true, gdy kategoria się zmieniła - wtedy parametry nowej kategorii przelicza dopiero krok
        /// parametrów, więc wystawienie czeka do kolejnego cyklu zamiast skończyć się brakiem parametrów.
        /// </summary>
        public async Task<bool> AlignBeforeCreateAsync(RolmarProduct product, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(product.AllegroId))
                return false;

            var before = product.DefaultAllegroCategory;
            var catalogCategory = await LeafCatalogCategoryAsync(product.AllegroId, ct);

            if (catalogCategory == null || catalogCategory == before)
                return false;

            await _productRepository.UpdateProductAllegroCategory(product.Id, catalogCategory.Value, ct);
            product.DefaultAllegroCategory = catalogCategory.Value;

            _logger.LogInformation("Default category of {Code} set to {Category} - the category of its Allegro product {ProductId}. Offer waits for parameters.",
                product.Code, catalogCategory, product.AllegroId);

            return true;
        }

        /// <summary>
        /// Uzupełnia patch przeniesienia o parametry bieżącej oferty, które kategoria docelowa zna, a których
        /// nie wyliczyliśmy sami. Lista jest pełna, więc zastępuje parametry starej kategorii.
        /// </summary>
        public static void ApplyTo(CategoryMove move, ProductOfferRequest request)
        {
            request.Category = new Category { Id = move.CategoryId.ToString(CultureInfo.InvariantCulture) };

            var parameters = request.Parameters ?? new List<Parameter>();
            var sent = parameters
                .SelectMany(p => new[] { p.Id, p.Name })
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            parameters.AddRange(move.RetainedParameters.Where(p => !sent.Contains(p.Id ?? string.Empty) && !sent.Contains(p.Name ?? string.Empty)));

            request.Parameters = parameters;
        }

        /// <summary>
        /// Obowiązkowe parametry kategorii docelowej, których zbudowany patch nie zawiera - ani w sekcji oferty,
        /// ani w produkcie. Przy produkcie z katalogu parametry opisujące produkt daje katalog.
        /// </summary>
        public static List<string> MissingIn(CategoryMove move, ProductOfferRequest request)
        {
            var sent = (request.Parameters ?? [])
                .Concat(request.ProductSet.SelectMany(s => s.ProductObject?.Parameters ?? []))
                .Where(p => p.Values.Any(v => !string.IsNullOrWhiteSpace(v)) || p.ValuesIds.Count > 0 || p.RangeValue != null)
                .SelectMany(p => new[] { p.Id, p.Name })
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return move.CategoryParameters
                .Where(p => move.LinkedToCatalog
                    ? p.Required && p.Options?.DescribesProduct != true
                    : p.Required || p.RequiredForProduct)
                .Where(p => !sent.Contains(p.Id ?? string.Empty) && !sent.Contains(p.Name ?? string.Empty))
                .Select(p => p.Name ?? p.Id ?? "-")
                .ToList();
        }

        /// <summary>Allegro odrzuciło przeniesienie - nie ponawiamy go przez <see cref="RetryRejectedAfter"/>.</summary>
        public void RecordRejected(string offerId, int categoryId)
        {
            var now = DateTime.UtcNow;
            RejectedMoves[(offerId, categoryId)] = now;

            foreach (var stale in RejectedMoves.Where(x => now - x.Value > RetryRejectedAfter).Select(x => x.Key).ToList())
                RejectedMoves.TryRemove(stale, out _);
        }

        /// <summary>
        /// Kategoria, w której mają stać oferty produktu na wszystkich kontach. Gdy produkt z katalogu stoi
        /// w innej kategorii niż zapisana przy produkcie, poprawiamy zapis - drugie konto wyznaczy tę samą.
        /// </summary>
        private async Task<int?> TargetCategoryAsync(RolmarProduct product, string? catalogProductId, int offerCategoryId, CancellationToken ct)
        {
            // Oferta w kategorii zapisanej przy produkcie - zgodna z resztą kont, nic nie pobieramy.
            if (product.DefaultAllegroCategory > 0 && offerCategoryId == product.DefaultAllegroCategory)
                return offerCategoryId;

            if (!string.IsNullOrWhiteSpace(catalogProductId))
            {
                var catalogCategory = await LeafCatalogCategoryAsync(catalogProductId, ct);

                if (catalogCategory == null)
                {
                    // Produkt z katalogu w kategorii ogólnej (Allegro nie przyjmuje w niej ofert) albo nieczytelny.
                    // Oferta zostaje, gdzie stoi. Jeśli przy produkcie zapisano kategorię ogólną, zastępujemy ją
                    // kategorią oferty - inaczej nowe oferty produktu powstawałyby w kategorii, której Allegro nie przyjmie.
                    if (offerCategoryId > 0 && product.DefaultAllegroCategory > 0
                        && await IsLeafAsync(product.DefaultAllegroCategory, ct) == false)
                    {
                        await _productRepository.UpdateProductAllegroCategory(product.Id, offerCategoryId, ct);

                        _logger.LogInformation("Default category of {Code} was the general category {General} - set to its offer category {Category}.",
                            product.Code, product.DefaultAllegroCategory, offerCategoryId);

                        product.DefaultAllegroCategory = offerCategoryId;
                    }

                    return null;
                }

                if (catalogCategory != product.DefaultAllegroCategory)
                {
                    await _productRepository.UpdateProductAllegroCategory(product.Id, catalogCategory.Value, ct);

                    _logger.LogInformation("Default category of {Code} set to {Category} - the category of its Allegro product {ProductId}.",
                        product.Code, catalogCategory, catalogProductId);

                    product.DefaultAllegroCategory = catalogCategory.Value;
                }

                return catalogCategory;
            }

            // Bez produktu z katalogu decyduje kategoria zapisana przy produkcie - o ile Allegro przyjmie
            // w niej ofertę, czyli jest liściem drzewa (do kategorii ogólnej Allegro nie przenosi).
            if (product.DefaultAllegroCategory <= 0)
                return null;

            return await IsLeafAsync(product.DefaultAllegroCategory, ct) == true ? product.DefaultAllegroCategory : null;
        }

        /// <summary>
        /// Parametry bieżącej oferty, które kategoria docelowa zna - wartości ustawione na Allegro (ręcznie albo
        /// dawniej przez serwis), których sami nie wyliczamy. <c>null</c>, gdy oferty nie da się odczytać.
        /// </summary>
        private async Task<List<Parameter>?> RetainedOfferParametersAsync(string offerId, IReadOnlyList<CategoryParameterItem> targetParameters, CancellationToken ct)
        {
            AllegroOfferDetails.Root? current;

            try
            {
                current = await _apiClient.GetAsync<AllegroOfferDetails.Root>($"/sale/product-offers/{offerId}", ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reading offer {OfferId} before moving it failed.", offerId);
                return null;
            }

            return current == null ? null : Retained(current.Parameters, targetParameters);
        }

        /// <summary>
        /// Parametry oferty, które kategoria docelowa zna jako parametry oferty, w postaci do wysłania. Parametru
        /// opisującego produkt nie przenosimy - w sekcji oferty Allegro go odrzuca, a daje go produkt.
        /// </summary>
        internal static List<Parameter> Retained(IEnumerable<AllegroOfferDetails.Parameter> current, IEnumerable<CategoryParameterItem> targetParameters)
        {
            var known = targetParameters
                .Where(p => p.Options?.DescribesProduct != true)
                .Select(p => p.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return current
                .Where(p => !string.IsNullOrWhiteSpace(p.Id) && known.Contains(p.Id!))
                .Where(p => p.Values.Count > 0 || p.ValuesIds.Count > 0 || p.RangeValue != null)
                .Select(p => new Parameter
                {
                    Id = p.Id,
                    Name = p.Name,
                    Values = p.ValuesIds.Count > 0 ? new List<string>() : p.Values.ToList(),
                    ValuesIds = p.ValuesIds.ToList(),
                    RangeValue = p.RangeValue == null ? null : new RangeValue { From = p.RangeValue.From, To = p.RangeValue.To }
                })
                .ToList();
        }

        /// <summary>
        /// Kategoria produktu z katalogu, o ile Allegro przyjmie w niej ofertę (liść drzewa). Produkty z katalogu
        /// bywają przypisane do kategorii ogólnej (WMU284 w 121620) - do takiej oferty nie przenosimy ani nie wystawiamy.
        /// </summary>
        private async Task<int?> LeafCatalogCategoryAsync(string catalogProductId, CancellationToken ct)
        {
            var categoryId = await CatalogCategoryAsync(catalogProductId, ct);

            return categoryId != null && await IsLeafAsync(categoryId.Value, ct) == true ? categoryId : null;
        }

        private async Task<int?> CatalogCategoryAsync(string catalogProductId, CancellationToken ct)
        {
            if (CatalogCategories.TryGetValue(catalogProductId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
                return cached.CategoryId;

            int? categoryId = null;

            try
            {
                var catalogProduct = await _apiClient.GetAsync<SearchProdustsResponse.Product>($"/sale/products/{catalogProductId}", ct);

                if (int.TryParse(catalogProduct?.Category?.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                    categoryId = id;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reading the category of Allegro product {ProductId} failed.", catalogProductId);
            }

            CatalogCategories[catalogProductId] = (DateTime.UtcNow + CacheTime, categoryId);
            return categoryId;
        }

        private async Task<bool?> IsLeafAsync(int categoryId, CancellationToken ct)
        {
            if (LeafCategories.TryGetValue(categoryId, out var cached) && DateTime.UtcNow < cached.ExpiresAt)
                return cached.Leaf;

            bool? leaf = null;

            try
            {
                leaf = (await _apiClient.GetAsync<CategoryInfo>($"/sale/categories/{categoryId}", ct))?.Leaf;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Reading Allegro category {CategoryId} failed.", categoryId);
            }

            LeafCategories[categoryId] = (DateTime.UtcNow + CacheTime, leaf);
            return leaf;
        }

        private sealed class CategoryInfo
        {
            [JsonPropertyName("leaf")]
            public bool Leaf { get; set; }
        }
    }

    /// <summary>
    /// Przeniesienie oferty: kategoria docelowa, nasze parametry tej kategorii, jej lista parametrów
    /// z Allegro i parametry bieżącej oferty, które kategoria docelowa zna.
    /// </summary>
    public sealed record CategoryMove(
        int CategoryId,
        List<ProductParameter> Parameters,
        IReadOnlyList<CategoryParameterItem> CategoryParameters,
        List<Parameter> RetainedParameters,
        bool LinkedToCatalog);
}
