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
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    public class GaskaApiService : IGaskaApiService
    {
        private readonly ServiceContext _service;
        private const int UpsertBatchSize = 1000;
        private const int UpsertBatchParallelism = 8;

        private const int CategoryMappingBatchSize = 2000;

        private readonly ILogger<GaskaApiService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly IImageRepository _imageRepo;
        private readonly ISyncCategoryRepository _syncCategoryRepo;
        private readonly HttpClient _http;
        private readonly AppSettings _appSettings;
        private IOptions<GaskaApiCredentials> _apiSettings;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public GaskaApiService(IProductRepository productRepo, IImageRepository imageRepo, ISyncCategoryRepository syncCategoryRepo, HttpClient http, IOptions<GaskaApiCredentials> apiSettings, IOptions<AppSettings> appSettings, ILogger<GaskaApiService> logger, ServiceContext serviceContext)
        {
            _service = serviceContext;
            _productRepo = productRepo;
            _imageRepo = imageRepo;
            _syncCategoryRepo = syncCategoryRepo;
            _http = http;
            _appSettings = appSettings.Value;
            _apiSettings = apiSettings;
            _logger = logger;
        }

        public async Task SyncProducts(CancellationToken ct = default)
        {
            // Produkty pobiera tylko to konto, więc bierzemy sumę kategorii skonfigurowanych na wszystkich kontach Allegro.
            var categoriesIds = await GetCategoriesToFetch(ct);

            if (categoriesIds.Count == 0)
            {
                _logger.LogWarning("No categories configured for any Allegro account. Skipping product sync.");
                return;
            }

            // Kategorie, pod którymi produkt został pobrany - na ich podstawie każde konto filtruje swoje oferty.
            var categoriesByProductCode = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            var fetchedCategories = new HashSet<int>();

            foreach (var categoryId in categoriesIds)
            {
                int page = 1;
                bool hasMore = true;
                bool categoryCompleted = true;
                var categoryProductCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (hasMore)
                {
                    try
                    {
                        var url = $"/products?category={categoryId}&page={page}&perPage={_apiSettings.Value.ProductsPerPage}&lng=pl";
                        var response = await _http.GetAsync(url);

                        if (!response.IsSuccessStatusCode)
                        {
                            // Przerywamy tę kategorię - przy trwałym błędzie API dalsze strony też nie odpowiedzą.
                            _logger.LogError($"API error while fetching page {page} for category {categoryId}: {response.StatusCode}");
                            categoryCompleted = false;
                            break;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var apiResponse = JsonSerializer.Deserialize<ProductsResponse>(json, _jsonOptions);

                        if (apiResponse?.Products == null || apiResponse.Products.Count == 0)
                        {
                            hasMore = false;
                            break;
                        }

                        try
                        {
                            var mappedProducts = apiResponse.Products
                                .Select(MapToRolmarProduct)
                                .GroupBy(p => p.Code, StringComparer.OrdinalIgnoreCase)
                                .Select(g => g.First())
                                .ToList();

                            await UpsertProductsInBatchesAsync(mappedProducts, ct);

                            foreach (var product in mappedProducts)
                            {
                                if (!string.IsNullOrWhiteSpace(product.Code))
                                    categoryProductCodes.Add(product.Code);
                            }

                            _logger.LogDebug("Fetched {Count} products for category {CategoryId}.", apiResponse.Products.Count, categoryId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error while saving products for category {categoryId}");
                            categoryCompleted = false;
                        }

                        if (apiResponse.Products.Count < _apiSettings.Value.ProductsPerPage)
                        {
                            hasMore = false;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error while getting products from page {page} for category {categoryId}.");
                        categoryCompleted = false;
                        break;
                    }
                    finally
                    {
                        page++;
                        await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductsInterval));
                    }
                }

                // Kategorię pobraną tylko częściowo pomijamy - inaczej skasowalibyśmy przypisania
                // produktów, których w tym przebiegu zwyczajnie nie zobaczyliśmy, i ich oferty zostałyby zakończone.
                if (!categoryCompleted)
                {
                    _logger.LogWarning(
                        "Category {CategoryId} was not fetched completely. Keeping current product assignments for this category.",
                        categoryId);
                    continue;
                }

                fetchedCategories.Add(categoryId);

                _logger.LogInformation("Category {CategoryId}: {Count} products fetched.", categoryId, categoryProductCodes.Count);

                foreach (var code in categoryProductCodes)
                {
                    if (!categoriesByProductCode.TryGetValue(code, out var productCategories))
                    {
                        productCategories = new HashSet<int>();
                        categoriesByProductCode.Add(code, productCategories);
                    }

                    productCategories.Add(categoryId);
                }
            }

            await SaveProductCategoriesAsync(categoriesByProductCode, fetchedCategories, ct);
        }

        private async Task<List<int>> GetCategoriesToFetch(CancellationToken ct)
        {
            var configuredCategories = _appSettings.CategoriesId?.Where(id => id != 0).ToList() ?? new List<int>();

            try
            {
                var accountsCategories = await _syncCategoryRepo.GetCompanyCategoriesAsync(ct);

                var categoriesIds = accountsCategories
                    .Select(c => int.TryParse(c, out var id) ? id : 0)
                    .Where(id => id != 0)
                    .Union(configuredCategories)
                    .Distinct()
                    .ToList();

                _logger.LogInformation(
                    "Fetching products for {Count} categories configured across all Allegro accounts: {Categories}",
                    categoriesIds.Count,
                    string.Join(", ", categoriesIds));

                return categoriesIds;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while reading categories of all Allegro accounts. Using categories of this service only.");
                return configuredCategories;
            }
        }

        private async Task SaveProductCategoriesAsync(Dictionary<string, HashSet<int>> categoriesByProductCode, HashSet<int> fetchedCategories, CancellationToken ct)
        {
            if (categoriesByProductCode.Count == 0 || fetchedCategories.Count == 0)
                return;

            try
            {
                foreach (var batch in categoriesByProductCode.Chunk(CategoryMappingBatchSize))
                {
                    await _syncCategoryRepo.ReplaceProductCategoriesAsync(
                        batch.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase),
                        fetchedCategories,
                        ct);
                }

                _logger.LogInformation("Saved supplier categories for {Count} products.", categoriesByProductCode.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while saving supplier categories of products.");
            }
        }

        private async Task UpsertProductsInBatchesAsync(List<RolmarProduct> products, CancellationToken ct)
        {
            if (products == null || products.Count == 0)
                return;

            var batches = products
                .Select((product, index) => new { product, index })
                .GroupBy(x => x.index / UpsertBatchSize)
                .Select(g => g.Select(x => x.product).ToList())
                .ToList();

            foreach (var batch in batches)
            {
                if (_productRepo is ProductRepository concreteRepo)
                {
                    await concreteRepo.UpsertProductsBatchAsync(batch, ct);
                }
                else
                {
                    foreach (var product in batch)
                    {
                        await _productRepo.UpsertProductAsync(product, ct);
                    }
                }
            }
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
                    await _productRepo.UpsertProductAsync(MapToRolmarProduct(existingProduct, apiResponse.Product), ct);

                    _logger.LogDebug("Product details updated for {ProductCode}.", existingProduct.Code);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while updating product {Id}.", productId);
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductInterval), ct);
                }
            }
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
                Categories = MapCategories(product.Categories)
            };
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
            var apiResponse = JsonSerializer.Deserialize<ProductsChangedReponse>(json, _jsonOptions);

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

        private static List<RolmarCategory> MapCategories(IEnumerable<ApiCategory>? categories)
        {
            if (categories == null)
                return new List<RolmarCategory>();

            var categoryList = categories.ToList();
            var parentIds = categoryList.Select(c => c.ParentID).ToHashSet();
            var leafCategories = categoryList.Where(c => !parentIds.Contains(c.Id)).ToList();
            var categoryLookup = categoryList
                .GroupBy(c => c.Id)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new List<RolmarCategory>();

            foreach (var category in leafCategories)
            {
                var name = BuildCategoryName(category, categoryLookup);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                result.Add(new RolmarCategory { Name = name });
            }

            return result
                .GroupBy(c => c.Name)
                .Select(g => g.First())
                .ToList();
        }

        private static string BuildCategoryName(ApiCategory category, IReadOnlyDictionary<int, ApiCategory> lookup)
        {
            var parts = new Stack<string>();
            var visited = new HashSet<int>();
            var current = category;

            while (current != null && visited.Add(current.Id))
            {
                if (!string.IsNullOrWhiteSpace(current.Name))
                    parts.Push(current.Name.Trim());

                if (current.ParentID == 0 || !lookup.TryGetValue(current.ParentID, out var parent))
                    break;

                current = parent;
            }

            return string.Join(" > ", parts);
        }
    }
}