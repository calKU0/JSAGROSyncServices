using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Allegro.JSAGRO.Gaska.ProductsService.DTOs;
using Allegro.JSAGRO.Gaska.ProductsService.Services.Gaska.Interfaces;
using Allegro.JSAGRO.Gaska.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Helpers;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Allegro.JSAGRO.Gaska.ProductsService.Services.GaskaApiService
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
                            foreach (var product in apiResponse.Products)
                            {
                                await _productRepo.UpsertProductAsync(MapToRolmarProduct(product), ct);
                            }
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

            //try
            //{
            //    var archivedCount = await _productRepo.ArchiveProductsNotIn(fetchedProductIds, ct);
            //    _logger.LogInformation($"Archived {archivedCount} products.");
            //}
            //catch (Exception ex)
            //{
            //    _logger.LogError(ex, "An error occurred while checking for products to archive.");
            //}
        }

        public async Task SyncProductDetails(CancellationToken ct = default)
        {
            List<RolmarProduct> productsToUpdate;

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
                    var response = await _http.GetAsync($"/product?id={product.IntegrationId}&lng=pl");

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"API error while fetching product details {product.Code}. Response Status: {response.StatusCode}");
                        continue;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var apiResponse = JsonSerializer.Deserialize<ProductResponse>(json, _jsonOptions);

                    if (apiResponse?.Product == null) continue;

                    await SaveProductImagesAsync(apiResponse.Product, product.Id, ct);
                    await _productRepo.UpsertProductAsync(MapToRolmarProduct(product, apiResponse.Product), ct);
                    _logger.LogInformation($"Successfully fetched and updated details of product {product.Code}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error while updating product {product.Code}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(_apiSettings.Value.ProductInterval));
                }
            }
        }

        private async Task SaveProductImagesAsync(ApiProduct product, int productId, CancellationToken ct)
        {
            if (product.Images == null || !product.Images.Any())
                return;

            foreach (var image in product.Images)
            {
                if (string.IsNullOrWhiteSpace(image?.Url))
                    continue;

                try
                {
                    var savedPath = await ImageHelper.SaveImageAsync(_http, image.Url, productId, ServiceConstants.ImagesFolder, ct);
                    if (string.IsNullOrWhiteSpace(savedPath))
                        _logger.LogWarning("Failed to save image for product {ProductId}. Url: {Url}", productId, image.Url);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to save image for product {ProductId}. Url: {Url}", productId, image.Url);
                }
            }
        }

        private static RolmarProduct MapToRolmarProduct(ApiProducts product)
        {
            return new RolmarProduct
            {
                Code = product.CodeGaska,
                CustomerCode = product.CodeCustomer,
                Name = product.Name,
                Description = product.Description + " " + product.TechnicalDetails,
                Ean = product.Ean,
                Weight = product.GrossWeight,
                SupplierName = product.SupplierName,
                SupplierLogo = product.SupplierLogo,
                InStock = product.InStock,
                Unit = product.Unit,
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
                    PackUnit = p.PackUnit,
                    PackQty = p.PackQty,
                    PackNettWeight = p.PackNettWeight,
                    PackGrossWeight = p.PackGrossWeight,
                    PackEan = p.PackEan,
                    PackRequired = p.PackRequired
                }).ToList() ?? new List<ProductPackage>(),
                Applications = product.Applications?.Select(a => new ProductApplication
                {
                    ApplicationId = a.Id,
                    ParentID = a.ParentID,
                    Name = a.Name
                }).ToList() ?? new List<ProductApplication>(),
                Specifications = MapSpecifications(product.Parameters),
                Categories = MapCategories(product.Categories)
            };
        }

        private static List<ProductSpecification> MapSpecifications(IEnumerable<ApiParameter>? parameters)
        {
            return parameters?
                .Where(p => !string.IsNullOrWhiteSpace(p.AttributeName))
                .Select(p =>
                {
                    var (name, unit) = SplitAttributeNameAndUnit(p.AttributeName);

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