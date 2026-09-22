using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.DTOs.Rolmar;
using JSAGROSyncServices.Products.Services.Suppliers;
using JSAGROSyncServices.Products.Settings;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Helpers;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    public class RolmarSyncService : IRolmarSyncService
    {
        private readonly ServiceContext _service;
        private const int UpsertBatchSize = 1000;
        private const int StockBatchSize = 5000;
        private const int ImageParallelism = 8;

        /// <summary>Co ile produktow wypisac postep pobierania zdjec.</summary>
        private const int ImageProgressEvery = 500;

        private readonly HttpClient _httpClient;
        private readonly ILogger<RolmarSyncService> _logger;
        private readonly IProductRepository _productRepository;
        private readonly ISyncCategoryRepository _syncCategoryRepository;
        private readonly ISupplierCategoryRepository _categoryRepository;
        private readonly RolmarApiCredentials _rolmarSettings;
        private readonly AppSettings _appSettings;

        public RolmarSyncService(HttpClient httpClient, ILogger<RolmarSyncService> logger, IProductRepository productRepository, ISyncCategoryRepository syncCategoryRepository, ISupplierCategoryRepository categoryRepository, IOptions<RolmarApiCredentials> options, IOptions<AppSettings> appSettings, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _httpClient = httpClient;
            _logger = logger;
            _productRepository = productRepository;
            _syncCategoryRepository = syncCategoryRepository;
            _categoryRepository = categoryRepository;
            _rolmarSettings = options.Value;
            _appSettings = appSettings.Value;
        }

        public async Task SyncProductsAsync(CancellationToken ct = default)
        {
            int upsertedCount = 0;
            int failedCount = 0;

            try
            {
                var body = new RolmarProductsRequest
                {
                    Data = new List<DataItem>
                    {
                        new DataItem
                        {
                            Param = new List<ParamItem>
                            {
                                new ParamItem { CategorySeparator = ">" }
                            }
                        }
                    }
                };

                var requestUri = $"v1/product/products.php?m=getProducts&lang=pl&wsKey={_rolmarSettings.ApiKey}";

                var options = new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var response = await _httpClient.PostAsJsonAsync(requestUri, body, options, ct);
                response.EnsureSuccessStatusCode();

                var rolmarResponseArray =
                    await response.Content.ReadFromJsonAsync<List<RolmarProductReponse>>(ct);

                if (rolmarResponseArray == null || !rolmarResponseArray.Any())
                {
                    _logger.LogWarning("No products found in Rolmar response.");
                    return;
                }

                var rolmarResponse = rolmarResponseArray[0];

                // Rolmar nie udostępnia listy kategorii, więc drzewo budujemy z kategorii WSZYSTKICH produktów
                // w odpowiedzi - także tych, których nie zapisujemy. Inaczej w konfiguratorze byłyby do wyboru
                // tylko kategorie już skonfigurowane i nie dałoby się dodać nowej.
                await SyncCategoryTreeAsync(rolmarResponse.Products, ct);

                // Produkty pobiera tylko to konto, więc bierzemy sumę kategorii skonfigurowanych na wszystkich kontach Allegro.
                var allowedCategories = await GetCategoriesToFetch(ct);

                if (allowedCategories.Count == 0)
                {
                    _logger.LogWarning("No categories configured for any Allegro account. Skipping product sync.");
                    return;
                }

                var mappedProducts = rolmarResponse.Products
                    .Where(product => product.Categories != null &&
                        product.Categories.Any(category => CategoryFilter.Matches(category, allowedCategories)))
                    .Select(MapToRolmarProduct)
                    .ToList();

                foreach (var batch in mappedProducts.Chunk(UpsertBatchSize))
                {
                    try
                    {
                        var batchList = batch.ToList();
                        await _productRepository.UpsertProductsBatchAsync(batchList, ct);

                        await _categoryRepository.ReplaceProductCategoriesAsync(
                            batchList
                                .Where(p => !string.IsNullOrWhiteSpace(p.Code))
                                .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                                .ToDictionary(g => g.Key, g => g.First().CategoryKeys ?? new List<string>(), StringComparer.OrdinalIgnoreCase),
                            ct);

                        upsertedCount += batchList.Count;
                    }
                    catch (Exception ex)
                    {
                        failedCount += batch.Length;
                        _logger.LogError(ex, "Error occurred while batch upserting Rolmar products.");
                    }
                }

                _logger.LogInformation("Product sync completed. Upserted: {Upserted}, Failed: {Failed}", upsertedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing products from Rolmar.");
            }
        }

        private async Task SyncCategoryTreeAsync(IEnumerable<ProductResult> products, CancellationToken ct)
        {
            try
            {
                var nodes = CategoryFilter.ToTreeNodes(products.SelectMany(p => p.Categories ?? new List<string>()));
                await _categoryRepository.UpsertNodesAsync(nodes, ct);

                _logger.LogInformation("Rolmar category tree synchronized: {Count} categories.", nodes.Count);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Bez odświeżonego drzewa działa filtr na dotychczasowym - produkty i tak pobieramy.
                _logger.LogError(ex, "Synchronizing the Rolmar category tree failed.");
            }
        }

        private async Task<List<string>> GetCategoriesToFetch(CancellationToken ct)
        {
            var configuredCategories = _appSettings.CategoriesName?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .ToList() ?? new List<string>();

            try
            {
                var categories = (await _syncCategoryRepository.GetCompanyCategoriesAsync(ct))
                    .Union(configuredCategories, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _logger.LogInformation(
                    "Fetching products for {Count} categories configured across all Allegro accounts: {Categories}",
                    categories.Count,
                    string.Join(", ", categories));

                return categories;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading categories of all Allegro accounts. Using categories of this service only.");
                return configuredCategories;
            }
        }

        public async Task SyncStockAsync(CancellationToken ct = default)
        {
            int updatedCount = 0;

            try
            {
                var products = await _productRepository.GetAllProducts(ct);
                var productCodes = products
                    .Select(p => p.Code)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var body = new RolmarStockRequest();
                var requestUri = $"v1/stock/stock.php?m=getStock&lang=pl&wsKey={_rolmarSettings.ApiKey}";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body, ct);
                response.EnsureSuccessStatusCode();

                var rolmarResponseArray =
                    await response.Content.ReadFromJsonAsync<List<RolmarStockResponse>>(ct);

                if (rolmarResponseArray == null || !rolmarResponseArray.Any())
                {
                    _logger.LogWarning("No stock data found in Rolmar response.");
                    return;
                }

                var rolmarResponse = rolmarResponseArray[0];

                // Stany aktualizujemy wsadowo - przy kilkudziesięciu tysiącach produktów
                // osobne wywołanie procedury na produkt zajmowało wielokrotnie więcej czasu niż całe pobranie danych.
                var stockUpdates = rolmarResponse.StockItems
                    .Where(stock => !string.IsNullOrWhiteSpace(stock.Index) && productCodes.Contains(stock.Index))
                    .Select(stock => new ProductStockUpdate(stock.Index!, stock.Stock))
                    .ToList();

                foreach (var batch in stockUpdates.Chunk(StockBatchSize))
                {
                    updatedCount += await _productRepository.UpdateProductStockBatchAsync(batch, ct);
                }

                _logger.LogInformation(
                    "Stock sync completed. Changed: {Updated}, Matched products: {Matched}",
                    updatedCount,
                    stockUpdates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing stock from Rolmar.");
            }
        }

        public async Task SyncImagesAsync(CancellationToken ct = default)
        {
            int downloaded = 0;
            int reused = 0;
            int failed = 0;
            int removed = 0;
            string? lastDownloadError = null;

            try
            {
                var products = await _productRepository.GetAllProducts(ct);
                var productsByCode = products
                    .Where(p => !string.IsNullOrWhiteSpace(p.Code))
                    .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var body = new RolmarImagesRequest();
                var requestUri = $"v1/photo/photo.php?m=getPhotos&lang=pl&wsKey={_rolmarSettings.ApiKey}";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body, ct);
                response.EnsureSuccessStatusCode();

                var rolmarResponseArray = await response.Content.ReadFromJsonAsync<List<RolmarImagesResponse>>(ct);

                if (rolmarResponseArray == null || rolmarResponseArray.Count == 0)
                {
                    _logger.LogWarning("No images found in Rolmar response.");
                    return;
                }

                var groups = rolmarResponseArray[0].PhotoItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Url) && !string.IsNullOrWhiteSpace(item.Index))
                    .GroupBy(item => item.Index!)
                    .Where(g => productsByCode.ContainsKey(g.Key))
                    .ToList();

                var totalPhotos = groups.Sum(g => g.Count());

                _logger.LogInformation(
                    "Rolmar images: {Products} products, {Photos} photos to check ({Parallelism} in parallel).",
                    groups.Count, totalPhotos, ImageParallelism);

                var processed = 0;
                var sw = Stopwatch.StartNew();

                // Pobieramy rownolegle - przy kilkudziesieciu tysiacach produktow sekwencyjne pobieranie
                // zdjec bylo najdluzszym krokiem calego cyklu.
                await Parallel.ForEachAsync(
                    groups,
                    new ParallelOptions { MaxDegreeOfParallelism = ImageParallelism, CancellationToken = ct },
                    async (group, token) =>
                    {
                        var product = productsByCode[group.Key];

                        var validUrls = group
                            .Select(item => item.Url)
                            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                                          (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                            .ToList();

                        if (validUrls.Count == 0)
                            return;

                        try
                        {
                            // Pobieramy tylko te zdjecia, ktorych jeszcze nie mamy na dysku.
                            var result = await ImageHelper.SaveNewImagesAsync(_httpClient, validUrls, product.Id, _service.ImagesFolder, token);

                            if (result.Failed > 0 && result.LastError != null)
                                lastDownloadError = result.LastError;

                            Interlocked.Add(ref downloaded, result.Downloaded);
                            Interlocked.Add(ref reused, result.Reused);
                            Interlocked.Add(ref failed, result.Failed);
                            Interlocked.Add(ref removed, result.Removed);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Add(ref failed, validUrls.Count);
                            _logger.LogError(ex, "Downloading images failed for {Code}.", product.Code);
                        }
                        finally
                        {
                            // Krok potrafi trwac dlugo - bez postepu log milczy i nie wiadomo, czy cos sie dzieje.
                            var done = Interlocked.Increment(ref processed);

                            if (done % ImageProgressEvery == 0)
                            {
                                _logger.LogInformation(
                                    "Rolmar images progress: {Done}/{Total} products, downloaded {Downloaded}, present {Reused}, failed {Failed} ({Elapsed}).",
                                    done, groups.Count, Volatile.Read(ref downloaded), Volatile.Read(ref reused), Volatile.Read(ref failed), sw.Elapsed);
                            }
                        }
                    });

                sw.Stop();

                if (failed > 0)
                {
                    _logger.LogError(
                        "Downloading {Failed} images failed (last error: {Error}). Existing files were kept.",
                        failed, lastDownloadError ?? "-");
                }

                _logger.LogInformation(
                    "Images downloaded: {Downloaded}, already present: {Reused}, failed: {Failed}, removed: {Removed}. Took {Elapsed}.",
                    downloaded, reused, failed, removed, sw.Elapsed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Syncing images from Rolmar failed.");
            }
        }

        private static RolmarProduct MapToRolmarProduct(ProductResult product)
        {
            var priceNet = decimal.TryParse(product.Price, out var pn) ? pn : 0m;
            var weight = float.TryParse(product.Weight, out var w) ? w : 0f;
            var package = decimal.TryParse(product.ErpPackage, out var pkg) ? pkg : 0m;

            return new RolmarProduct
            {
                Code = product.ProductIndex ?? string.Empty,
                Name = product.Name ?? string.Empty,
                Description = product.Description,
                Ean = product.Ean,
                Weight = weight,
                Fits = product.Fits,
                Substitutes = product.Substitutes,
                Unit = product.Unit ?? string.Empty,
                CurrencyPrice = product.Currency,
                PriceNet = priceNet,
                PriceGross = priceNet * 1.23m,
                Package = package,
                IntegrationId = Convert.ToInt32(product.Id),
                Specifications = product.Specifications?.Select(s => new JSAGROSyncServices.Contracts.Models.ProductSpecification
                {
                    Name = s.Name ?? string.Empty,
                    Value = s.Value ?? string.Empty,
                    UnitName = s.UnitName ?? string.Empty
                }).ToList() ?? new List<JSAGROSyncServices.Contracts.Models.ProductSpecification>(),
                // Rolmar zawsze podaje kategorie produktu, więc ustawiamy listę (także pustą) - zapis zastąpi przypisania.
                CategoryKeys = (product.Categories ?? new List<string>())
                    .Select(CategoryFilter.Normalize)
                    .Where(key => key.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }
    }
}