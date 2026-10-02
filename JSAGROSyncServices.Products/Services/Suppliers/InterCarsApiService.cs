using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Helpers;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.DTOs.InterCars;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    /// <summary>
    /// Pobieranie katalogu Inter Cars do bazy. Lista katalogu daje nazwę, opis i markę,
    /// a wycena (<c>/inventory/quote</c>) - cenę zakupu, stany w magazynach, blokadę zwrotu
    /// i EAN-y, po 100 SKU na zapytanie. Wagę i wymiary API zwraca dopiero na pytanie
    /// o pojedyncze SKU - stąd osobny, porcjowany krok szczegółów.
    /// </summary>
    public class InterCarsApiService : IInterCarsApiService
    {
        private const string CategoryPath = "/ic/catalog/category";
        private const string ProductsPath = "/ic/catalog/products";
        private const string QuotePath = "/ic/inventory/quote";

        /// <summary>Prefiks liści katalogu. Zapytanie o ich podkategorie zwraca rodzeństwo, więc nie schodzimy niżej.</summary>
        private const string LeafKeyPrefix = "GenericArticle_";

        /// <summary>
        /// Największy numer strony przyjmowany przez API (strony liczone od zera). Razem z limitem
        /// 100 pozycji na stronę daje to twardy sufit 10 000 produktów na kategorię - głębsza strona
        /// kończy się błędem ICF306, więc z liczniejszych kategorii pobieramy tylko tyle.
        /// </summary>
        private const int MaxPageNumber = 99;

        /// <summary>Zabezpieczenie przed cyklem w drzewie kategorii od dostawcy.</summary>
        private const int MaxTreeDepth = 30;
        private readonly HttpClient _http;
        private readonly IProductRepository _productRepo;
        private readonly ISupplierCategoryRepository _categoryRepo;
        private readonly ISyncCategoryRepository _syncCategoryRepo;
        private readonly IInterCarsDataFileService _dataFiles;
        private readonly ServiceContext _service;
        private readonly InterCarsApiCredentials _api;
        private readonly AppSettings _appSettings;
        private readonly ILogger<InterCarsApiService> _logger;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public InterCarsApiService(
            HttpClient http,
            IProductRepository productRepo,
            ISupplierCategoryRepository categoryRepo,
            ISyncCategoryRepository syncCategoryRepo,
            IInterCarsDataFileService dataFiles,
            ServiceContext service,
            IOptions<InterCarsApiCredentials> api,
            IOptions<AppSettings> appSettings,
            ILogger<InterCarsApiService> logger)
        {
            _http = http;
            _productRepo = productRepo;
            _categoryRepo = categoryRepo;
            _syncCategoryRepo = syncCategoryRepo;
            _dataFiles = dataFiles;
            _service = service;
            _api = api.Value;
            _appSettings = appSettings.Value;
            _logger = logger;
        }

        // ---------------------------------------------------------------- produkty

        public async Task SyncProductsAsync(CancellationToken ct = default)
        {
            // Produkty pobiera tylko ten serwis, więc bierzemy sumę kategorii wszystkich kont Allegro.
            var configured = await GetCategoriesToFetchAsync(ct);

            if (configured.Count == 0)
            {
                _logger.LogWarning("No categories configured for any Allegro account. Skipping product sync.");
                return;
            }

            // Katalog API obejmuje cały asortyment Inter Cars i nie da się go zawęzić do części
            // rolniczych. Robią to dopiero pliki CSV - zawierają wyłącznie SKU asortymentu AGRO.
            // Bez tej listy nie ruszamy pobierania: do bazy trafiłby cały katalog dostawcy.
            var allowedSkus = await _dataFiles.GetAllowedSkusAsync(ct);

            if (allowedSkus == null)
            {
                _logger.LogError("The Inter Cars AGRO SKU list could not be read - product sync was skipped to avoid importing the whole catalog.");
                return;
            }

            // Drzewo najpierw - na nim opiera się filtr ofert, lista wyboru w konfiguratorze
            // i podział pobierania na kategorie, do których faktycznie przypisane są produkty.
            var tree = await SyncCategoryTreeAsync(configured, ct);
            var categoriesToFetch = tree.GetProductCategories(configured);

            if (categoriesToFetch.Count == 0)
            {
                _logger.LogWarning(
                    "None of the configured categories was found in the Inter Cars catalog: {Categories}.",
                    string.Join(", ", configured));
                return;
            }

            var sw = Stopwatch.StartNew();
            var categoriesBySku = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int fetched = 0, saved = 0, skipped = 0, outsideWhitelist = 0, failedCategories = 0;

            foreach (var categoryKey in categoriesToFetch)
            {
                ct.ThrowIfCancellationRequested();

                var result = await FetchCategoryProductsAsync(categoryKey, allowedSkus, categoriesBySku, ct);

                fetched += result.Fetched;
                saved += result.Saved;
                skipped += result.Skipped;
                outsideWhitelist += result.OutsideWhitelist;

                if (!result.Completed)
                {
                    failedCategories++;
                    _logger.LogWarning("Category {Category} was not fetched completely ({Count} products).", categoryKey, result.Fetched);
                }
            }

            await SaveProductCategoriesAsync(categoriesBySku, ct);

            sw.Stop();

            _logger.LogInformation(
                "Inter Cars products: {Saved} saved, {Skipped} skipped (no price), {Outside} outside the AGRO list, {Fetched} returned from {Categories} categories (incomplete: {Failed}). Took {Elapsed}.",
                saved, skipped, outsideWhitelist, fetched, categoriesToFetch.Count, failedCategories, sw.Elapsed);

            await ArchiveMissingProductsAsync(categoriesBySku.Keys, configured, failedCategories, ct);
        }

        /// <summary>
        /// Odnotowuje, że dostawca nadal ma pobrane produkty w ofercie, i archiwizuje te, których
        /// nie oddał od kilku dni. Produkt archiwalny nie trafia na Allegro, a jego oferta jest kończona.
        ///
        /// Przy niekompletnym pobraniu (awaria API w części kategorii) pomijamy archiwizację -
        /// produkty tych kategorii wyglądałyby na wycofane, choć u dostawcy nadal są.
        /// </summary>
        private async Task ArchiveMissingProductsAsync(
            IEnumerable<string> seenSkus,
            IReadOnlyCollection<string> configured,
            int failedCategories,
            CancellationToken ct)
        {
            try
            {
                await _productRepo.MarkProductsSeenAsync(seenSkus, ct);

                if (failedCategories > 0)
                {
                    _logger.LogWarning("{Count} categories were fetched incompletely - archiving of missing products was skipped this cycle.", failedCategories);
                    return;
                }

                var archived = await _productRepo.ArchiveMissingProductsAsync(_appSettings.ArchiveAfterDaysMissing, configured, ct);

                if (archived > 0)
                {
                    _logger.LogInformation(
                        "{Count} products have been missing from the Inter Cars catalog for over {Days} days - marked as archived, their offers will be ended.",
                        archived, _appSettings.ArchiveAfterDaysMissing);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Archiving products missing from the Inter Cars catalog failed.");
            }
        }

        /// <summary>
        /// Wszystkie strony jednej kategorii. Każda strona od razu trafia do bazy - katalog bywa duży.
        /// Produkty spoza listy AGRO (<paramref name="allowedSkus"/>) odrzucamy zaraz po odczytaniu
        /// strony, więc nie kosztują ani zapytania o wycenę, ani miejsca w bazie.
        /// </summary>
        private async Task<CategoryFetchResult> FetchCategoryProductsAsync(
            string categoryKey,
            IReadOnlySet<string> allowedSkus,
            Dictionary<string, List<string>> categoriesBySku,
            CancellationToken ct)
        {
            var perPage = Math.Clamp(_api.ProductsPerPage, 1, 100);
            int fetched = 0, saved = 0, skipped = 0, outside = 0;

            for (var page = 0; page <= MaxPageNumber; page++)
            {
                ct.ThrowIfCancellationRequested();

                InterCarsProductsResponse? response;

                try
                {
                    response = await GetAsync<InterCarsProductsResponse>(
                        $"{ProductsPath}?categoryId={Uri.EscapeDataString(categoryKey)}&pageNumber={page}&pageSize={perPage}", ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Fetching page {Page} of Inter Cars category {Category} failed.", page, categoryKey);
                    return new CategoryFetchResult(fetched, saved, skipped, outside, Completed: false);
                }

                var returned = response?.Products
                    .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
                    .GroupBy(p => p.Sku!, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList() ?? new List<InterCarsProduct>();

                // Pusta strona kończy kategorię, ale strona złożona wyłącznie z produktów spoza
                // listy AGRO - już nie: kolejne strony nadal mogą zawierać nasz asortyment.
                if (returned.Count == 0)
                    return new CategoryFetchResult(fetched, saved, skipped, outside, Completed: true);

                fetched += returned.Count;

                var products = returned.Where(p => allowedSkus.Contains(p.Sku!)).ToList();
                outside += returned.Count - products.Count;

                foreach (var product in products)
                {
                    if (!categoriesBySku.TryGetValue(product.Sku!, out var keys))
                        categoriesBySku[product.Sku!] = keys = new List<string>();

                    if (!keys.Contains(categoryKey, StringComparer.OrdinalIgnoreCase))
                        keys.Add(categoryKey);
                }

                if (products.Count > 0)
                {
                    var (savedInPage, skippedInPage) = await SaveProductsAsync(products, ct);

                    saved += savedInPage;
                    skipped += skippedInPage;
                }

                if (response?.HasNextPage != true)
                    return new CategoryFetchResult(fetched, saved, skipped, outside, Completed: true);
            }

            // Niekompletne, choc bez bledu: produktow powyzej limitu nie zobaczylismy, wiec nie
            // wolno ich uznac za wycofane u dostawcy. Completed=true archiwizowaloby cala reszte
            // kategorii razem z jej ofertami.
            _logger.LogWarning(
                "Category {Category} is larger than the Inter Cars page limit - only the first {Count} products were fetched. " +
                "Archiving is skipped this cycle; configure deeper categories to stay under the limit.",
                categoryKey, (MaxPageNumber + 1) * perPage);

            return new CategoryFetchResult(fetched, saved, skipped, outside, Completed: false);
        }

        /// <summary>Podsumowanie pobrania jednej kategorii katalogu.</summary>
        private readonly record struct CategoryFetchResult(int Fetched, int Saved, int Skipped, int OutsideWhitelist, bool Completed);

        /// <summary>
        /// Uzupełnia produkty o cenę i stan, po czym zapisuje je wsadowo. Produkt bez ceny pomijamy -
        /// zapisanie zera zepsułoby cenę oferty i wyłączyło produkt z aktualizacji.
        /// </summary>
        private async Task<(int Saved, int Skipped)> SaveProductsAsync(List<InterCarsProduct> products, CancellationToken ct)
        {
            var saved = 0;
            var skipped = 0;

            foreach (var batch in products.Chunk(SkuBatchSize))
            {
                var skus = batch.Select(p => p.Sku!).ToList();

                var quotes = await GetQuotesAsync(skus, ct);

                if (quotes == null)
                {
                    // Wycena się nie udała - zostawiamy te produkty bez zmian do następnego cyklu.
                    skipped += batch.Length;
                    continue;
                }

                var existing = await _productRepo.GetProductsByCodesAsync(skus, ct);

                var mapped = new List<RolmarProduct>(batch.Length);
                var unavailable = new List<ProductStockUpdate>();

                foreach (var product in batch)
                {
                    if (!quotes.TryGetValue(product.Sku!, out var quote))
                    {
                        // Wycena obejmuje wyłącznie towary dostępne do kupienia - brak pozycji
                        // oznacza zerowy stan. Taki produkt zerujemy, żeby jego oferta się zakończyła.
                        skipped++;
                        unavailable.Add(new ProductStockUpdate(product.Sku!, 0));
                        continue;
                    }

                    existing.TryGetValue(product.Sku!, out var current);
                    mapped.Add(MapProduct(product, quote, current));
                }

                if (mapped.Count > 0)
                {
                    await _productRepo.UpsertProductsBatchAsync(mapped, ct);
                    saved += mapped.Count;
                }

                if (unavailable.Count > 0)
                    await _productRepo.UpdateProductStockBatchAsync(unavailable, ct);
            }

            return (saved, skipped);
        }

        private async Task SaveProductCategoriesAsync(Dictionary<string, List<string>> categoriesBySku, CancellationToken ct)
        {
            if (categoriesBySku.Count == 0)
                return;

            try
            {
                // Zapis zastępuje przypisania, więc idzie raz, na komplecie kategorii produktu -
                // ten sam produkt potrafi wyjść z kilku kategorii katalogu.
                await _categoryRepo.ReplaceProductCategoriesAsync(categoriesBySku, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Saving Inter Cars product categories failed.");
            }
        }

        // ---------------------------------------------------------------- stany magazynowe

        public async Task SyncStockAsync(CancellationToken ct = default)
        {
            try
            {
                // Stanu produktow wycofanych u dostawcy nie odpytujemy - ich oferty i tak konczymy.
                var codes = (await _productRepo.GetAllProducts(ct))
                    .Where(p => p.ArchivedAt == null)
                    .Select(p => p.Code)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (codes.Count == 0)
                {
                    _logger.LogInformation("No Inter Cars products in the database - skipping stock sync.");
                    return;
                }

                var sw = Stopwatch.StartNew();
                var updates = new List<ProductStockUpdate>(codes.Count);
                var failedBatches = 0;

                foreach (var batch in codes.Chunk(SkuBatchSize))
                {
                    ct.ThrowIfCancellationRequested();

                    var quotes = await GetQuotesAsync(batch, ct);

                    if (quotes == null)
                    {
                        // Nieudane zapytanie to nie jest zerowy stan - te produkty zostawiamy bez zmian.
                        failedBatches++;
                        continue;
                    }

                    // Produkt, którego nie ma już w żadnym z naszych magazynów, musi dostać zero -
                    // inaczej oferta zostałaby aktywna na towarze, którego nie kupimy.
                    foreach (var code in batch)
                        updates.Add(new ProductStockUpdate(code, quotes.TryGetValue(code, out var quote) ? Availability(quote) : 0));
                }

                var changed = 0;

                foreach (var batch in updates.Chunk(StockUpdateBatchSize))
                    changed += await _productRepo.UpdateProductStockBatchAsync(batch, ct);

                sw.Stop();

                if (failedBatches > 0)
                {
                    _logger.LogWarning(
                        "Inter Cars stock: {Failed} batches could not be read - their stock was left unchanged.",
                        failedBatches);
                }

                _logger.LogInformation(
                    "Inter Cars stock: {Changed} products changed out of {Total} checked (warehouses: {Warehouses}). Took {Elapsed}.",
                    changed, updates.Count, string.Join(", ", Warehouses), sw.Elapsed);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Syncing stock from Inter Cars failed.");
            }
        }

        // ---------------------------------------------------------------- szczegóły produktów

        public async Task SyncProductDetailsAsync(CancellationToken ct = default)
        {
            List<string> codes;

            try
            {
                // Inter Cars nie ogranicza liczby zapytań, więc bierzemy komplet produktów wymagających
                // szczegółów - tempo wyznaczają Parallelism i RequestDelayMilliseconds, a nie dzienna porcja.
                codes = await _productRepo.GetProductCodesForDetailUpdate(_api.ProductDetailsPerRun, _api.ProductDetailsRefreshDays, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reading the Inter Cars product detail queue failed.");
                return;
            }

            if (codes.Count == 0)
            {
                _logger.LogInformation("No Inter Cars products need details today.");
                return;
            }

            _logger.LogInformation("Inter Cars product details: {Count} products to fetch.", codes.Count);

            var sw = Stopwatch.StartNew();
            int updated = 0, missing = 0;

            foreach (var batch in codes.Chunk(SkuBatchSize))
            {
                ct.ThrowIfCancellationRequested();

                var details = await GetProductDetailsAsync(batch, ct);

                if (details.Count == 0)
                {
                    missing += batch.Length;
                    continue;
                }

                var skus = details.Keys.ToList();
                var quotes = await GetQuotesAsync(skus, ct);

                if (quotes == null)
                {
                    // Bez wyceny nie ma czego zapisac - te produkty zostaja w kolejce.
                    missing += batch.Length;
                    continue;
                }

                var existing = await _productRepo.GetProductsByCodesAsync(skus, ct);

                var mapped = new List<RolmarProduct>(details.Count);
                var withoutQuote = new List<string>();

                foreach (var (sku, detail) in details)
                {
                    if (!quotes.TryGetValue(sku, out var quote))
                    {
                        // Towar wyprzedany: szczegoly mamy, ale bez ceny nie ma czego zapisac.
                        // Mimo to znaczymy go jako pobrany - inaczej blokowalby czolo kolejki
                        // w kazdym przebiegu i reszta katalogu nigdy by sie nie odswiezyla.
                        withoutQuote.Add(sku);
                        continue;
                    }

                    existing.TryGetValue(sku, out var current);
                    mapped.Add(MapProductWithDetails(detail, quote, current));
                }

                if (mapped.Count > 0)
                    await _productRepo.UpsertProductsBatchAsync(mapped, ct);

                // Znacznik idzie po zapisie danych - inaczej nieudany zapis wypadłby z kolejki.
                var fetched = mapped.Select(p => p.Code).Concat(withoutQuote).ToList();

                if (fetched.Count > 0)
                    await _productRepo.MarkDetailsFetched(fetched, ct);

                updated += mapped.Count;
                missing += batch.Length - mapped.Count;
            }

            sw.Stop();

            _logger.LogInformation(
                "Inter Cars product details: updated {Updated}, without details or price {Missing}. Took {Elapsed}.",
                updated, missing, sw.Elapsed);
        }

        private async Task<IReadOnlyDictionary<string, InterCarsProduct>> GetProductDetailsAsync(IReadOnlyCollection<string> skus, CancellationToken ct)
        {
            var details = new ConcurrentDictionary<string, InterCarsProduct>(StringComparer.OrdinalIgnoreCase);

            // API przyjmuje tylko jedno SKU na zapytanie, więc jedyne, co przyspiesza ten krok,
            // to równoległość - ograniczona, bo przy zbyt wielu zapytaniach API odpowiada 429.
            await Parallel.ForEachAsync(skus, ParallelOptions(ct), async (sku, token) =>
            {
                try
                {
                    var response = await GetAsync<InterCarsProductsResponse>(
                        $"{ProductsPath}?sku={Uri.EscapeDataString(sku)}", token);

                    var product = response?.Products.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Sku));

                    if (product != null)
                        details[product.Sku!] = product;
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Fetching Inter Cars details of {Sku} failed.", sku);
                }
            });

            return details;
        }

        // ---------------------------------------------------------------- kategorie

        private async Task<List<string>> GetCategoriesToFetchAsync(CancellationToken ct)
        {
            var configured = _appSettings.GetConfiguredCategories(IntegrationCompany.InterCars);

            try
            {
                var categories = (await _syncCategoryRepo.GetCompanyCategoriesAsync(ct))
                    .Union(configured, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _logger.LogInformation(
                    "Fetching products for {Count} categories configured across all Allegro accounts: {Categories}",
                    categories.Count, string.Join(", ", categories));

                return categories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading categories of all Allegro accounts. Using categories of this service only.");
                return configured;
            }
        }

        /// <summary>
        /// Pełne drzewo kategorii - wszystkie poziomy katalogu. Kosztuje ponad tysiąc zapytań,
        /// więc idzie raz na dobę, w oknie nocnym. Służy wyłącznie liście wyboru w konfiguratorze:
        /// zakres pobierania produktów wyznaczają skonfigurowane gałęzie, a te i tak schodzą do końca.
        /// </summary>
        public async Task SyncCategoryTreeAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var tree = new CategoryTree();

            try
            {
                await CrawlAsync(null, MaxTreeDepth, tree, ct);
                await _categoryRepo.UpsertNodesAsync(tree.Nodes, ct);

                sw.Stop();

                _logger.LogInformation(
                    "Full Inter Cars category tree synchronized: {Count} categories. Took {Elapsed}.",
                    tree.Nodes.Count, sw.Elapsed);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Częściowe drzewo też jest coś warte - zapisujemy, co zdążyliśmy pobrać.
                _logger.LogError(ex, "Synchronizing the full Inter Cars category tree failed after {Count} categories.", tree.Nodes.Count);

                try
                {
                    await _categoryRepo.UpsertNodesAsync(tree.Nodes, ct);
                }
                catch (Exception saveEx) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError(saveEx, "Saving the partially fetched Inter Cars category tree failed.");
                }
            }
        }

        /// <summary>
        /// Odświeża skonfigurowane gałęzie drzewa kategorii - to z nich pobieramy produkty.
        /// Resztę katalogu, potrzebną wyłącznie liście wyboru w Menadżerze Serwisów, pobiera
        /// w całości krok dzienny.
        /// </summary>
        private async Task<CategoryTree> SyncCategoryTreeAsync(IReadOnlyCollection<string> configured, CancellationToken ct)
        {
            var tree = new CategoryTree();

            try
            {
                // Pierwsze uruchomienie: w bazie nie ma jeszcze żadnej kategorii, a krok dzienny
                // ruszy dopiero w nocy - bez tego lista wyboru w Menadżerze Serwisów byłaby pusta.
                if (await _categoryRepo.CountNodesAsync(ct) == 0)
                {
                    _logger.LogInformation("The Inter Cars category tree is empty - fetching the whole catalog.");
                    await CrawlAsync(null, MaxTreeDepth, tree, ct);
                }

                foreach (var key in configured)
                    await CrawlAsync(key, MaxTreeDepth, tree, ct);

                await _categoryRepo.UpsertNodesAsync(tree.Nodes, ct);

                _logger.LogInformation("Inter Cars category tree synchronized: {Count} categories.", tree.Nodes.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Bez odświeżonego drzewa pracujemy na dotychczasowym - produkty i tak pobieramy.
                _logger.LogError(ex, "Synchronizing the Inter Cars category tree failed.");
            }

            return tree;
        }

        /// <summary>Schodzi w głąb katalogu wszerz, do zadanej głębokości. <paramref name="root"/> null = kategorie główne.</summary>
        private async Task CrawlAsync(string? root, int maxDepth, CategoryTree tree, CancellationToken ct)
        {
            var current = new List<string?> { root };

            for (var depth = 0; depth < maxDepth && current.Count > 0; depth++)
            {
                var next = new List<string?>();

                foreach (var parent in current)
                {
                    ct.ThrowIfCancellationRequested();

                    // Liść katalogu odpytany o podkategorie zwraca własne rodzeństwo - schodzenie niżej
                    // zapętliłoby pobieranie.
                    if (parent != null && parent.StartsWith(LeafKeyPrefix, StringComparison.Ordinal))
                        continue;

                    // Węzeł pobrany już w tym cyklu (płytkie przejście po całym katalogu) - niżej
                    // schodzimy po tym, co jest w drzewie, zamiast pytać API drugi raz.
                    if (parent != null && !tree.MarkExpanded(parent))
                    {
                        next.AddRange(tree.ChildrenOf(parent));
                        continue;
                    }

                    var children = await GetAsync<List<InterCarsCategory>>(
                        parent == null ? CategoryPath : $"{CategoryPath}?categoryId={Uri.EscapeDataString(parent)}", ct);

                    foreach (var child in children ?? new List<InterCarsCategory>())
                    {
                        if (string.IsNullOrWhiteSpace(child.CategoryId) || string.IsNullOrWhiteSpace(child.Label))
                            continue;

                        if (string.Equals(child.CategoryId, parent, StringComparison.OrdinalIgnoreCase))
                            continue;

                        tree.Add(child.CategoryId!, parent, child.Label!.Trim());
                        next.Add(child.CategoryId!);
                    }
                }

                current = next;
            }
        }

        // ---------------------------------------------------------------- ceny i stany

        /// <summary>
        /// Wycena maksymalnie 100 SKU: cena zakupu po rabatach, stany w magazynach, blokada zwrotu
        /// i EAN-y w jednym zapytaniu. W odpowiedzi są wyłącznie towary dostępne do kupienia.
        /// </summary>
        private async Task<Dictionary<string, InterCarsQuoteItem>?> GetQuotesAsync(IReadOnlyCollection<string> skus, CancellationToken ct)
        {
            var quotes = new Dictionary<string, InterCarsQuoteItem>(StringComparer.OrdinalIgnoreCase);

            if (skus.Count == 0)
                return quotes;

            var request = new InterCarsQuoteRequest
            {
                Lines = skus.Select(sku => new InterCarsQuoteLine { Sku = sku, Quantity = 1 }).ToList()
            };

            try
            {
                var items = await PostAsync<List<InterCarsQuoteItem>>(QuotePath, request, ct);

                foreach (var item in items ?? new List<InterCarsQuoteItem>())
                {
                    if (string.IsNullOrWhiteSpace(item.Sku) || item.Price == null || item.Price.CustomerPriceNet <= 0)
                        continue;

                    quotes[item.Sku!] = item;
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // null = nie wiemy nic. Pusty słownik znaczyłby "żaden z tych towarów nie jest dostępny",
                // a wtedy zerowalibyśmy stany i pokończyli oferty przez chwilową awarię API.
                _logger.LogError(ex, "Fetching Inter Cars quotes for {Count} SKUs failed.", skus.Count);
                return null;
            }

            return quotes;
        }

        /// <summary>Stan produktu to suma dostępności ze skonfigurowanych magazynów.</summary>
        private int Availability(InterCarsQuoteItem quote)
        {
            var warehouses = Warehouses;

            return quote.Lines
                .Where(line => line.Availability > 0)
                .Where(line => warehouses.Count == 0 || warehouses.Contains(line.Location ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                .Sum(line => line.Availability);
        }

        // ---------------------------------------------------------------- mapowanie

        /// <summary>
        /// Produkt z listy katalogu. Waga, EAN i numer katalogowy przychodzą dopiero ze szczegółami,
        /// więc przepisujemy je z bazy - inaczej każdy cykl kasowałby to, co zebrał krok szczegółów.
        /// </summary>
        private RolmarProduct MapProduct(InterCarsProduct product, InterCarsQuoteItem quote, RolmarProduct? existing)
        {
            var price = quote.Price!;

            return new RolmarProduct
            {
                Code = product.Sku!,
                CustomerCode = product.Index,
                Name = BuildName(product),
                Description = product.Description,
                SupplierName = product.Brand,
                Ean = FirstEan(quote.Eans) ?? existing?.Ean,
                Weight = existing?.Weight ?? 0,
                Substitutes = existing?.Substitutes,
                InStock = Availability(quote),
                Unit = DefaultUnit,
                Package = 1,
                CurrencyPrice = price.CurrencyCode ?? DefaultCurrency,
                PriceNet = price.CustomerPriceNet,
                PriceGross = price.CustomerPriceGross,
                // Blokadę zwrotu bierzemy z wyceny: lista katalogu zwraca w tym polu zawsze false.
                BlockedReturn = quote.BlockedReturn || product.BlockedReturn,
                IntegrationCompany = IntegrationCompany.InterCars,
                // null = "nie znamy w tej operacji": zapis nie rusza specyfikacji ani kategorii.
                Specifications = null,
                CategoryKeys = null
            };
        }

        /// <summary>Produkt ze szczegółów - tu API podaje komplet danych, więc nadpisujemy wszystko.</summary>
        private RolmarProduct MapProductWithDetails(InterCarsProduct product, InterCarsQuoteItem quote, RolmarProduct? existing)
        {
            var mapped = MapProduct(product, quote, existing);

            mapped.Ean = FirstEan(product.Eans) ?? mapped.Ean;
            mapped.Weight = ParseDecimal(product.PackageWeight) is { } weight ? (float)weight : existing?.Weight ?? 0;
            // Numer katalogowy producenta pomaga kupującym odnaleźć część - trafia w opis oferty
            // i w parametr z numerami zamienników, tak jak numery zamienne u pozostałych dostawców.
            mapped.Substitutes = string.IsNullOrWhiteSpace(product.ArticleNumber) ? existing?.Substitutes : product.ArticleNumber;
            mapped.Specifications = BuildDimensions(product);

            return mapped;
        }

        /// <summary>
        /// Wymiary opakowania jako specyfikacje - na nich opiera się wybór cennika dostawy
        /// i dopłata za produkt ponadgabarytowy. Inter Cars podaje je w centymetrach.
        /// </summary>
        private static List<ProductSpecification> BuildDimensions(InterCarsProduct product)
        {
            var dimensions = new List<(string Name, string? Value)>
            {
                ("Długość", product.PackageLength ?? product.PackageDepth),
                ("Szerokość", product.PackageWidth),
                ("Wysokość", product.PackageHeight)
            };

            return dimensions
                .Where(d => ParseDecimal(d.Value) > 0)
                .Select(d => new ProductSpecification
                {
                    Name = d.Name,
                    Value = ParseDecimal(d.Value)!.Value.ToString("0.##", CultureInfo.InvariantCulture),
                    UnitName = "cm"
                })
                .ToList();
        }

        /// <summary>
        /// Nazwa produktu: <c>shortDescription</c> to nazwa grupy asortymentowej ("Filtr oleju"),
        /// <c>description</c> - konkretny wariant. Bierzemy pierwszą sensowną.
        /// </summary>
        private static string BuildName(InterCarsProduct product)
        {
            var name = product.ShortDescription?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                name = product.Description?.Trim();

            return string.IsNullOrWhiteSpace(name) ? product.Sku ?? string.Empty : name;
        }

        private static string? FirstEan(IEnumerable<string>? eans) =>
            eans?.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))?.Trim();

        private static decimal? ParseDecimal(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return decimal.TryParse(value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        // ---------------------------------------------------------------- HTTP

        private const string DefaultUnit = "szt";
        private const string DefaultCurrency = "PLN";
        private const int StockUpdateBatchSize = 5000;

        private int SkuBatchSize => Math.Clamp(_api.SkuBatchSize, 1, 100);

        private List<string> Warehouses => _api.Warehouses?
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Select(w => w.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        private ParallelOptions ParallelOptions(CancellationToken ct) => new()
        {
            MaxDegreeOfParallelism = Math.Max(_api.Parallelism, 1),
            CancellationToken = ct
        };

        private Task<T?> GetAsync<T>(string path, CancellationToken ct) =>
            SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, path), ct);

        private Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) =>
            SendAsync<T>(() => new HttpRequestMessage(HttpMethod.Post, path)
            {
                Content = JsonContent.Create(body, options: _jsonOptions)
            }, ct);

        /// <summary>
        /// Zapytanie z ponowieniem według wspólnej polityki (<see cref="HttpRetryPolicy"/>) -
        /// Inter Cars odrzuca nadmiar zapytań kodem 429, a przy przeciążeniu odpowiada 5xx.
        /// </summary>
        private async Task<T?> SendAsync<T>(Func<HttpRequestMessage> requestFactory, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = requestFactory();
                using var response = await _http.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(ct);

                    // Odstęp po każdym zapytaniu - API szybko zaczyna odrzucać serie kodem 429.
                    if (_api.RequestDelayMilliseconds > 0)
                        await Task.Delay(_api.RequestDelayMilliseconds, ct);

                    return string.IsNullOrWhiteSpace(json)
                        ? default
                        : JsonSerializer.Deserialize<T>(json, _jsonOptions);
                }

                var path = request.RequestUri?.PathAndQuery ?? "-";

                if (attempt >= HttpRetryPolicy.MaxAttempts || !HttpRetryPolicy.ShouldRetry(response.StatusCode))
                {
                    if (HttpRetryPolicy.ShouldRetry(response.StatusCode))
                        HttpRetryPolicy.LogGaveUp(_logger, "Inter Cars", request.Method, path, response.StatusCode, attempt);

                    var body = await response.Content.ReadAsStringAsync(ct);

                    throw new HttpRequestException(
                        $"Inter Cars {request.Method} {path} returned {(int)response.StatusCode}: {Shorten(body)}");
                }

                var delay = HttpRetryPolicy.GetDelay(response, attempt);

                HttpRetryPolicy.LogRetry(_logger, "Inter Cars", request.Method, path, response.StatusCode, attempt, delay);

                await Task.Delay(delay, ct);
            }
        }

        private static string Shorten(string body) => body.Length <= 300 ? body : body[..300];
    }

    /// <summary>
    /// Drzewo kategorii zbudowane w trakcie pobierania. Pamięta rodziców, żeby wskazać kategorie,
    /// z których faktycznie pobieramy produkty.
    /// </summary>
    internal sealed class CategoryTree
    {
        /// <summary>Zabezpieczenie przed cyklem w danych od dostawcy.</summary>
        private const int MaxDepth = 30;

        private readonly Dictionary<string, SupplierCategoryNode> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

        public List<SupplierCategoryNode> Nodes => _nodes.Values.ToList();

        /// <summary>Dodaje węzeł. Zwraca <c>false</c>, gdy już go znamy - wtedy nie schodzimy w niego drugi raz.</summary>
        public bool Add(string key, string? parentKey, string name)
        {
            if (_nodes.ContainsKey(key))
                return false;

            _nodes[key] = new SupplierCategoryNode(key, parentKey, name);
            return true;
        }

        /// <summary>Zwraca <c>false</c>, gdy węzeł był już rozwijany - zabezpieczenie przed cyklem.</summary>
        public bool MarkExpanded(string key) => _expanded.Add(key);

        /// <summary>Znane podkategorie węzła.</summary>
        public IEnumerable<string> ChildrenOf(string key) => _nodes.Values
            .Where(n => string.Equals(n.ParentSourceKey, key, StringComparison.OrdinalIgnoreCase))
            .Select(n => n.SourceKey);

        /// <summary>
        /// Kategorie, z których pobieramy produkty: najgłębsze węzły klasyfikacji sprzedażowej
        /// w skonfigurowanych gałęziach. Produkty widać na każdym poziomie (węzeł nadrzędny zwraca
        /// sumę podrzędnych), ale dopiero najgłębszy daje produktowi dokładną kategorię.
        /// </summary>
        public List<string> GetProductCategories(IReadOnlyCollection<string> configured)
        {
            // Liście katalogu (GenericArticle) pomijamy: mają tylko część produktów swojego rodzica,
            // więc pobieranie z nich zgubiłoby resztę asortymentu kategorii.
            var childrenByParent = _nodes.Values
                .Where(n => n.ParentSourceKey != null && !IsLeafKey(n.SourceKey))
                .ToLookup(n => n.ParentSourceKey!, n => n.SourceKey, StringComparer.OrdinalIgnoreCase);

            var result = new List<string>();

            foreach (var key in configured)
            {
                // Skonfigurowany korzeń bywa w drzewie tylko jako rodzic: przejście zaczynane od
                // niego dodaje jego dzieci, ale jego samego już nie. Obecność dzieci znaczy więc
                // to samo co obecność węzła - inaczej schodzilibyśmy o poziom za wysoko i
                // pobierali całą gałąź jednym zapytaniem, które API ucina na 10 000 produktach.
                if (!_nodes.ContainsKey(key) && !childrenByParent.Contains(key))
                {
                    // Kategorii spoza pobranego drzewa nie odrzucamy - drzewo mogło się nie pobrać,
                    // a produkty z niej i tak chcemy mieć.
                    result.Add(key);
                    continue;
                }

                CollectDeepest(key, childrenByParent, result, 0);
            }

            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void CollectDeepest(string key, ILookup<string, string> childrenByParent, List<string> result, int depth)
        {
            var children = childrenByParent[key].ToList();

            if (children.Count == 0 || depth >= MaxDepth)
            {
                result.Add(key);
                return;
            }

            foreach (var child in children)
                CollectDeepest(child, childrenByParent, result, depth + 1);
        }

        private static bool IsLeafKey(string key) => key.StartsWith("GenericArticle_", StringComparison.Ordinal);
    }
}
