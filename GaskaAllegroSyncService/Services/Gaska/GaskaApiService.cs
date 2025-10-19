using GaskaAllegroSyncService.DTOs;
using GaskaAllegroSyncService.Models.Product;
using GaskaAllegroSyncService.Repositories.Interfaces;
using GaskaAllegroSyncService.Services.Gaska.Interfaces;
using GaskaAllegroSyncService.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;

namespace GaskaAllegroSyncService.Services.GaskaApiService
{
    public class GaskaApiService : IGaskaApiService
    {
        private readonly ILogger<GaskaApiService> _logger;
        private readonly IProductRepository _productRepo;
        private readonly HttpClient _http;
        private readonly List<int> _categoriesIds;
        private IOptions<GaskaApiCredentials> _apiSettings;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public GaskaApiService(IProductRepository productRepo, HttpClient http, IOptions<GaskaApiCredentials> apiSettings, IOptions<AppSettings> appSettings, ILogger<GaskaApiService> logger)
        {
            _productRepo = productRepo;
            _http = http;
            _categoriesIds = appSettings.Value.CategoriesId.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s =>
                           {
                               if (int.TryParse(s.Trim(), out int val))
                                   return val;
                               return 0;
                           })
                           .Where(v => v != 0)
                           .ToList() ?? new List<int>();
            _apiSettings = apiSettings;
            _logger = logger;
        }

        public async Task SyncProducts(CancellationToken ct = default)
        {
            HashSet<int> fetchedProductIds = new HashSet<int>();
            bool hasErrors = false;

            foreach (var categoryId in _categoriesIds)
            {
                int page = 1;
                bool hasMore = true;

                while (hasMore)
                {
                    try
                    {
                        var url = $"/products?category={categoryId}&page={page}&perPage={_apiSettings.Value.ProductsPerPage}&lng=pl";
                        var response = await _http.GetAsync(url);

                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.LogError($"API error while fetching page {page} for category {categoryId}: {response.StatusCode}");
                            hasErrors = true;
                            continue;
                        }

                        var json = await response.Content.ReadAsStringAsync();
                        var apiResponse = JsonSerializer.Deserialize<ProductsResponse>(json, _jsonOptions);

                        if (apiResponse.Products == null || apiResponse.Products.Count == 0)
                        {
                            hasMore = false;
                            break;
                        }

                        try
                        {
                            fetchedProductIds.UnionWith(apiResponse.Products.Select(p => p.Id));

                            await _productRepo.UpsertProducts(apiResponse.Products, fetchedProductIds, ct);
                            _logger.LogInformation($"Successfully fetched and updated {apiResponse.Products.Count} products for category {categoryId}.");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Error while saving products for category {categoryId}");
                            hasErrors = true;
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
                        hasErrors = true;
                        break;
                    }
                    finally
                    {
                        page++;
                        await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductsInterval));
                    }
                }
            }

            if (hasErrors)
            {
                _logger.LogWarning("Errors occurred during product sync. Archiving skipped to avoid data inconsistency.");
                return;
            }

            try
            {
                var archivedCount = await _productRepo.ArchiveProductsNotIn(fetchedProductIds, ct);
                _logger.LogInformation($"Archived {archivedCount} products.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking for products to archive.");
            }
        }

        public async Task SyncProductDetails(CancellationToken ct = default)
        {
            List<Product> productsToUpdate;

            try
            {
                productsToUpdate = await _productRepo.GetProductsForDetailUpdate(_apiSettings.Value.ProductPerDay, ct);
                if (!productsToUpdate.Any()) return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error while getting products to update details from database");
                return;
            }

            foreach (var product in productsToUpdate)
            {
                try
                {
                    var response = await _http.GetAsync($"/product?id={product.Id}&lng=pl");

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"API error while fetching product details. Product name: {product.Name}. Product Code: {product.CodeGaska}. Response Status: {response.StatusCode}");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var apiResponse = JsonSerializer.Deserialize<ProductResponse>(json, _jsonOptions);

                    if (apiResponse?.Product == null) continue;

                    await _productRepo.UpdateProductDetails(product.Id, apiResponse.Product, ct);
                    _logger.LogInformation($"Successfully fetched and updated details of product {product.Name} ({product.CodeGaska})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while updating product details. Product name: {product.Name} Code: {product.CodeGaska}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductInterval));
                }
            }
        }
    }
}