using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.DTOs.Gaska;
using JSAGROSyncServices.Products.Repositories;
using JSAGROSyncServices.Products.Services.Suppliers;
using JSAGROSyncServices.Products.Settings;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Helpers;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    public class GaskaApiService : IGaskaApiService
    {
        private readonly ServiceContext _service;
        private const int UpsertBatchSize = 1000;

        private readonly ILogger<GaskaApiService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly IImageRepository _imageRepo;
        private readonly ISupplierCategoryRepository _categoryRepo;
        private readonly ISyncCategoryRepository _syncCategoryRepo;
        private readonly HttpClient _http;
        private readonly AppSettings _appSettings;
        private IOptions<GaskaApiCredentials> _apiSettings;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public GaskaApiService(IProductRepository productRepo, IImageRepository imageRepo, ISupplierCategoryRepository categoryRepo, ISyncCategoryRepository syncCategoryRepo, HttpClient http, IOptions<GaskaApiCredentials> apiSettings, IOptions<AppSettings> appSettings, ILogger<GaskaApiService> logger, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _productRepo = productRepo;
            _imageRepo = imageRepo;
            _categoryRepo = categoryRepo;
            _syncCategoryRepo = syncCategoryRepo;
            _http = http;
            _appSettings = appSettings.Value;
            _apiSettings = apiSettings;
            _logger = logger;
        }

        public async Task SyncProducts(CancellationToken ct = default)
        {
            // Drzewo kategorii najpierw - na nim opiera się filtr ofert i lista wyboru w konfiguratorze.
            await SyncCategoryTreeAsync(ct);

            // Pobieramy i zapisujemy wyłącznie produkty ze skonfigurowanych kategorii. Produkty pobiera
            // tylko ten serwis, więc bierzemy sumę kategorii wszystkich kont Allegro.
            var categoryIds = await GetCategoriesToFetchAsync(ct);

            if (categoryIds.Count == 0)
            {
                _logger.LogWarning("No categories configured for any Allegro account. Skipping product sync.");
                return;
            }

            int fetched = 0, incomplete = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sw = Stopwatch.StartNew();

            foreach (var categoryId in categoryIds)
            {
                ct.ThrowIfCancellationRequested();

                var (codes, completed) = await FetchCategoryAsync(categoryId, ct);
                fetched += codes.Count;
                seen.UnionWith(codes);

                if (completed)
                {
                    _logger.LogInformation("Category {CategoryId}: {Count} products fetched.", categoryId, codes.Count);
                }
                else
                {
                    incomplete++;
                    _logger.LogWarning(
                        "Category {CategoryId} was not fetched completely ({Count} products).",
                        categoryId, codes.Count);
                }
            }

            sw.Stop();

            _logger.LogInformation(
                "Gąska products fetched: {Fetched} from {Categories} categories (incomplete: {Incomplete}). Took {Elapsed}.",
                fetched, categoryIds.Count, incomplete, sw.Elapsed);

            await MarkSeenAndArchiveAsync(seen, categoryIds, incomplete, ct);
        }

        /// <summary>
        /// Odnotowuje obecność produktów u dostawcy i archiwizuje te, których nie oddał od kilku dni.
        /// Produkt archiwalny nie trafia na Allegro, a jego oferta jest kończona.
        ///
        /// Przy niekompletnym pobraniu pomijamy archiwizację - produkty kategorii, której API nie oddało
        /// do końca, wyglądałyby na wycofane, choć u dostawcy nadal są.
        /// </summary>
        private async Task MarkSeenAndArchiveAsync(
            IReadOnlyCollection<string> seenCodes,
            IReadOnlyCollection<int> categoryIds,
            int incompleteCategories,
            CancellationToken ct)
        {
            try
            {
                await _productRepo.MarkProductsSeenAsync(seenCodes, ct);

                if (incompleteCategories > 0)
                {
                    _logger.LogWarning("{Count} categories were fetched incompletely - archiving of missing products was skipped this cycle.", incompleteCategories);
                    return;
                }

                var categories = categoryIds.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
                var archived = await _productRepo.ArchiveMissingProductsAsync(_appSettings.ArchiveAfterDaysMissing, categories, ct);

                if (archived > 0)
                {
                    _logger.LogInformation(
                        "{Count} products have been missing from the Gąska catalog for over {Days} days - marked as archived, their offers will be ended.",
                        archived, _appSettings.ArchiveAfterDaysMissing);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Archiving products missing from the Gąska catalog failed.");
            }
        }

        /// <summary>Pobiera wszystkie strony jednej kategorii. Zwraca kody produktów i to, czy pobranie się udało.</summary>
        private async Task<(HashSet<string> Codes, bool Completed)> FetchCategoryAsync(int categoryId, CancellationToken ct)
        {
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var perPage = _apiSettings.Value.ProductsPerPage;
            var interval = TimeSpan.FromSeconds(_apiSettings.Value.ProductsInterval);

            for (var page = 1; ; page++)
            {
                ct.ThrowIfCancellationRequested();

                bool lastPage;

                try
                {
                    var response = await _http.GetAsync($"/products?category={categoryId}&page={page}&perPage={perPage}&lng=pl", ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        // Przy trwałym błędzie API dalsze strony też nie odpowiedzą.
                        _logger.LogError("Gąska API error on page {Page} of category {CategoryId}: {Status}.", page, categoryId, response.StatusCode);
                        return (codes, false);
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var apiResponse = JsonSerializer.Deserialize<ProductsResponse>(json, _jsonOptions);
                    var products = apiResponse?.Products ?? new List<ApiProducts>();

                    var mappedProducts = products
                        .Select(MapToRolmarProduct)
                        .Where(p => !string.IsNullOrWhiteSpace(p.Code))
                        .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                        .ToList();

                    await UpsertProductsInBatchesAsync(mappedProducts, ct);

                    foreach (var product in mappedProducts)
                        codes.Add(product.Code);

                    lastPage = products.Count < perPage;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error while fetching page {Page} of Gąska category {CategoryId}.", page, categoryId);
                    return (codes, false);
                }

                // Odstęp po każdym zapytaniu, także ostatnim - kolejna kategoria zaczyna od razu.
                await Task.Delay(interval, ct);

                if (lastPage)
                    return (codes, true);
            }
        }

        private async Task<List<int>> GetCategoriesToFetchAsync(CancellationToken ct)
        {
            var configured = _appSettings.CategoriesId?.Where(id => id != 0).ToList() ?? new List<int>();

            try
            {
                var allAccounts = (await _syncCategoryRepo.GetCompanyCategoriesAsync(ct))
                    .Select(c => int.TryParse(c, out var id) ? id : 0)
                    .Where(id => id != 0);

                var categoryIds = allAccounts.Union(configured).Distinct().OrderBy(id => id).ToList();

                _logger.LogInformation("Fetching products for {Count} categories configured across all Allegro accounts: {Categories}",
                    categoryIds.Count, string.Join(", ", categoryIds));

                return categoryIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading categories of all Allegro accounts. Using categories of this service only.");
                return configured;
            }
        }

        /// <summary>
        /// Pełne drzewo kategorii Gąski z /categories. Jedno zapytanie na cykl - dzięki temu
        /// lista kategorii do wyboru jest kompletna, nawet dla kategorii bez pobranych produktów.
        /// </summary>
        private async Task SyncCategoryTreeAsync(CancellationToken ct)
        {
            try
            {
                var response = await _http.GetAsync("/categories?lng=pl", ct);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Gąska API error while fetching categories: {Status}.", response.StatusCode);
                    return;
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                var apiResponse = JsonSerializer.Deserialize<CategoriesResponse>(json, _jsonOptions);

                if (apiResponse == null || apiResponse.Result != 0)
                {
                    _logger.LogError("Gąska API returned an error for categories: {Message}", apiResponse?.Message ?? "empty response");
                    return;
                }

                var nodes = ToCategoryNodes(apiResponse.Categories);
                await _categoryRepo.UpsertNodesAsync(nodes, ct);

                _logger.LogInformation("Gąska category tree synchronized: {Count} categories.", nodes.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Bez aktualnego drzewa działa filtr na dotychczasowym - nie przerywamy pobierania produktów.
                _logger.LogError(ex, "Synchronizing the Gąska category tree failed.");
            }
        }

        private async Task UpsertProductsInBatchesAsync(List<RolmarProduct> products, CancellationToken ct)
        {
            foreach (var batch in products.Chunk(UpsertBatchSize))
                await _productRepo.UpsertProductsBatchAsync(batch.ToList(), ct);
        }

        public async Task SyncProductDetails(CancellationToken ct = default)
        {
            List<int> productsToUpdate;

            try
            {
                productsToUpdate = await _productRepo.GetProductsForDetailUpdate(_apiSettings.Value.ProductPerDay, ct);

                if (!productsToUpdate.Any() || productsToUpdate.Count < _apiSettings.Value.ProductPerDay)
                {
                    var remainingSlots = _apiSettings.Value.ProductPerDay - productsToUpdate.Count;

                    if (remainingSlots > 0)
                    {
                        var productsChanged = await GetProductsChanged(DateTime.Now.AddDays(-2), ct);
                        if (productsChanged != null && productsChanged.Any())
                        {
                            productsToUpdate.AddRange(productsChanged.Take(remainingSlots));
                        }
                    }
                }

                if (!productsToUpdate.Any())
                {
                    _logger.LogInformation("No products found for detail update today.");
                    return;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while getting products to update details from database.");
                return;
            }

            _logger.LogInformation("Gąska product details: {Count} products to fetch.", productsToUpdate.Count);

            int updated = 0, failed = 0;
            var sw = Stopwatch.StartNew();

            foreach (var productId in productsToUpdate)
            {
                try
                {
                    var url = $"/product?id={productId}&lng=pl";
                    var response = await _http.GetAsync(url, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError("API error while fetching product details for {Id}. Response Status: {StatusCode}", productId, response.StatusCode);
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    var apiResponse = JsonSerializer.Deserialize<ProductResponse>(json, _jsonOptions);

                    if (apiResponse?.Product == null)
                    {
                        _logger.LogWarning("Product details returned null for {Id}. Skipping update.", productId);
                        continue;
                    }

                    var existingProduct = await _productRepo.GetProductByIntegrationIdAsync(productId, ct);
                    if (existingProduct == null)
                    {
                        _logger.LogDebug("Product with IntegrationId {Id} not found in database. Skipping update.", productId);
                        continue;
                    }

                    await SaveProductImagesAsync(apiResponse.Product, existingProduct.Id, ct);

                    var product = MapToRolmarProduct(existingProduct, apiResponse.Product);
                    await _productRepo.UpsertProductAsync(product, ct);

                    // Bez kategorii produkt trafi do kolejki szczegółów ponownie - warto to widzieć w logu.
                    if (product.CategoryKeys!.Count == 0)
                        _logger.LogWarning("Product {ProductCode} has no categories in its details.", existingProduct.Code);

                    // Kategorie zapisujemy jako ostatnie: ich obecność oznacza "szczegóły pobrane",
                    // więc nie może wyprzedzić zdjęć ani reszty danych produktu.
                    await _categoryRepo.ReplaceProductCategoriesAsync(
                        new Dictionary<string, List<string>> { [existingProduct.Code] = product.CategoryKeys ?? new List<string>() },
                        ct);

                    updated++;

                    _logger.LogDebug("Product details updated for {ProductCode}.", existingProduct.Code);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Error while updating product {Id}.", productId);
                }
                finally
                {
                    if (!ct.IsCancellationRequested)
                        await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductInterval), ct);
                }
            }

            _logger.LogInformation("Gąska product details: updated {Updated}, failed {Failed}. Took {Elapsed}.", updated, failed, sw.Elapsed);
        }

        private async Task SaveProductImagesAsync(ApiProduct product, int productId, CancellationToken ct)
        {
            if (product.Images == null || !product.Images.Any())
                return;

            var urls = product.Images
                .Where(i => !string.IsNullOrWhiteSpace(i.Url))
                .Select(i => i.Url)
                .ToList();

            if (!urls.Any())
                return;

            // Pobieramy tylko brakujące zdjęcia. Adresy wysłane do Allegro kasujemy dopiero wtedy,
            // gdy zestaw plików faktycznie się zmienił - inaczej co cykl wgrywalibyśmy te same zdjęcia od nowa.
            var result = await ImageHelper.SaveNewImagesAsync(_http, urls, productId, _service.ImagesFolder, ct);

            if (result.Downloaded > 0 || result.Removed > 0)
                await _imageRepo.DeleteProductImagesAsync(productId, ct);

            if (result.Failed > 0)
                _logger.LogWarning("Downloading {Count} images failed for {Code} ({Error}).", result.Failed, product.CodeGaska, result.LastError ?? "-");
        }

        private static RolmarProduct MapToRolmarProduct(ApiProducts product)
        {
            return new RolmarProduct
            {
                Code = product.CodeGaska ?? string.Empty,
                CustomerCode = product.CodeCustomer,
                Name = product.Name ?? string.Empty,
                Description = product.Description + " " + product.TechnicalDetails,
                Ean = product.Ean,
                Weight = product.GrossWeight,
                SupplierName = product.SupplierName,
                SupplierLogo = product.SupplierLogo,
                InStock = product.InStock,
                Unit = product.Unit ?? string.Empty,
                CurrencyPrice = product.CurrencyPrice,
                PriceNet = product.NetPrice,
                PriceGross = product.GrossPrice,
                DeliveryType = product.DeliveryType,
                IntegrationId = product.Id
            };
        }

        private static RolmarProduct MapToRolmarProduct(RolmarProduct existing, ApiProduct product)
        {
            return new RolmarProduct
            {
                Id = existing.Id,
                Code = product.CodeGaska ?? existing.Code,
                CustomerCode = product.CodeCustomer ?? existing.CustomerCode,
                Name = product.Name ?? existing.Name,
                Description = existing.Description,
                Ean = existing.Ean,
                Weight = existing.Weight,
                SupplierName = product.SupplierName ?? existing.SupplierName,
                SupplierLogo = product.SupplierLogo ?? existing.SupplierLogo,
                Substitutes = product.CrossNumbers != null
                    ? string.Join(',', product.CrossNumbers.Select(c => c.CrossNumber).Where(c => !string.IsNullOrWhiteSpace(c)))
                    : existing.Substitutes,
                InStock = product.InStock,
                Unit = product.Packages?.Where(p => p.PackRequired == 1).Select(p => p.PackUnit).FirstOrDefault() ?? existing.Unit,
                CurrencyPrice = product.CurrencyPrice ?? existing.CurrencyPrice,
                Package = product.Packages?.Where(p => p.PackRequired == 1).Select(p => Convert.ToDecimal(p.PackQty)).FirstOrDefault() ?? 1,
                PriceNet = product.PriceNet,
                PriceGross = product.PriceGross,
                DeliveryType = product.DeliveryType,
                IntegrationId = product.Id,
                Packages = product.Packages?.Select(p => new ProductPackage
                {
                    PackUnit = p.PackUnit ?? string.Empty,
                    PackQty = p.PackQty,
                    PackNettWeight = p.PackNettWeight,
                    PackGrossWeight = p.PackGrossWeight,
                    PackEan = p.PackEan ?? string.Empty,
                    PackRequired = p.PackRequired
                }).ToList() ?? new List<ProductPackage>(),
                Applications = product.Applications?.Select(a => new ProductApplication
                {
                    ApplicationId = a.Id,
                    ParentID = a.ParentID,
                    Name = a.Name ?? string.Empty
                }).ToList() ?? new List<ProductApplication>(),
                Specifications = MapSpecifications(product.Parameters),
                CategoryKeys = MapCategoryKeys(product.Categories)
            };
        }

        /// <summary>
        /// Kategorie produktu ze szczegółów - wyłącznie liście, czyli te, które nie są rodzicem
        /// innej kategorii z tej listy. Nadrzędne wynikają z drzewa, więc ich nie zapisujemy.
        /// </summary>
        private static List<string> MapCategoryKeys(IEnumerable<ApiCategory>? categories)
        {
            var all = categories?.Where(c => c.Id != 0).ToList();

            if (all == null || all.Count == 0)
                return new List<string>();

            var parentIds = all.Select(c => c.ParentID).ToHashSet();

            return all
                .Where(c => !parentIds.Contains(c.Id))
                .Select(c => c.Id.ToString(CultureInfo.InvariantCulture))
                .Distinct()
                .ToList();
        }

        private async Task<List<int>?> GetProductsChanged(DateTime dateFrom, CancellationToken ct)
        {
            var url = $"/productsChanged?dateFrom={dateFrom:yyyy-MM-dd}";
            var response = await _http.GetAsync(url, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("API error while fetching products changed from {DateFrom}", dateFrom);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var apiResponse = JsonSerializer.Deserialize<ProductsChangedResponse>(json, _jsonOptions);

            if (apiResponse?.Products == null || !apiResponse.Products.Any())
            {
                _logger.LogWarning("No products changed from {DateFrom}.", dateFrom);
                return null;
            }

            return apiResponse.Products.Select(p => p.TwrId).ToList();
        }

        private static List<ProductSpecification> MapSpecifications(IEnumerable<ApiParameter>? parameters)
        {
            return parameters?
                .Where(p => !string.IsNullOrWhiteSpace(p.AttributeName))
                .Select(p =>
                {
                    var (name, unit) = SplitAttributeNameAndUnit(p.AttributeName!);

                    return new ProductSpecification
                    {
                        Name = name,
                        Value = p.AttributeValue?.Trim() ?? string.Empty,
                        UnitName = unit
                    };
                })
                .ToList() ?? new List<ProductSpecification>();
        }

        private static (string Name, string Unit) SplitAttributeNameAndUnit(string attributeName)
        {
            if (string.IsNullOrWhiteSpace(attributeName))
                return (string.Empty, string.Empty);

            var trimmed = attributeName.Trim();

            // Match unit in trailing parentheses, e.g. "Szerokość (mm)" -> ("Szerokość", "mm")
            var match = Regex.Match(trimmed, @"^(?<name>.*)\s*\((?<unit>[^()]*)\)\s*$");
            if (!match.Success)
                return (trimmed, string.Empty);

            var unit = match.Groups["unit"].Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(unit))
                return (trimmed, string.Empty);

            var name = match.Groups["name"].Value?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                name = trimmed;

            return (name, unit);
        }

        /// <summary>
        /// Kategorie z /categories -> węzły drzewa. Kluczem jest id Gąski; parentID = 0 oznacza korzeń.
        /// </summary>
        private static List<SupplierCategoryNode> ToCategoryNodes(IEnumerable<ApiCategory>? categories) =>
            (categories ?? Enumerable.Empty<ApiCategory>())
                .Where(c => c.Id != 0 && !string.IsNullOrWhiteSpace(c.Name))
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .Select(c => new SupplierCategoryNode(
                    c.Id.ToString(CultureInfo.InvariantCulture),
                    c.ParentID == 0 || c.ParentID == c.Id ? null : c.ParentID.ToString(CultureInfo.InvariantCulture),
                    c.Name!.Trim()))
                .ToList();
    }
}