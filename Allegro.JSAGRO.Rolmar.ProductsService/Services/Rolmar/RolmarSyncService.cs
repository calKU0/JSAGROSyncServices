using Allegro.JSAGRO.Rolmar.ProductsService.DTOs;
using Allegro.JSAGRO.Rolmar.ProductsService.DTOs.Rolmar;
using Allegro.JSAGRO.Rolmar.ProductsService.Services.Interfaces;
using Allegro.JSAGRO.Rolmar.ProductsService.Settings;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Services.Rolmar
{
    public class RolmarSyncService : IRolmarSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RolmarSyncService> _logger;
        private readonly IProductRepository _productRepository;
        private readonly RolmarApiCredentials _rolmarSettings;
        private readonly AppSettings _appSettings;

        public RolmarSyncService(HttpClient httpClient, ILogger<RolmarSyncService> logger, IProductRepository productRepository, IOptions<RolmarApiCredentials> options, IOptions<AppSettings> appSettings)
        {
            _httpClient = httpClient;
            _logger = logger;
            _productRepository = productRepository;
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

                var allowedCategories = _appSettings.CategoriesName
                    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList() ?? new List<string>();

                foreach (var product in rolmarResponse.Products)
                {
                    try
                    {
                        if (product.Categories == null ||
                            !product.Categories.Any(c =>
                                allowedCategories.Any(ac =>
                                    c.StartsWith(ac, StringComparison.OrdinalIgnoreCase))))
                            continue;

                        var mappedProduct = MapToRolmarProduct(product);
                        bool success = await _productRepository.UpsertProductAsync(mappedProduct, ct);

                        if (success)
                        {
                            upsertedCount++;
                        }
                        else
                        {
                            failedCount++;
                            _logger.LogWarning("Failed to upsert product with Code: {Code}", product.ProductIndex);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogError(ex, "Error occurred while upserting product with Code: {Code}", product.ProductIndex);
                    }
                }

                _logger.LogInformation(
                    "Product sync completed. Upserted: {Upserted}, Failed: {Failed}",
                    upsertedCount,
                    failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing products from Rolmar.");
            }
        }

        public async Task SyncStockAsync(CancellationToken ct = default)
        {
            int updatedCount = 0;
            int failedCount = 0;

            try
            {
                var products = await _productRepository.GetAllProducts(ct);
                var productCodes = products.Select(p => p.Code).ToHashSet();

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

                foreach (var stock in rolmarResponse.StockItems)
                {
                    try
                    {
                        if (!productCodes.Contains(stock.Index))
                            continue;

                        bool success =
                            await _productRepository.UpdateProductStockAsync(
                                stock.Index,
                                stock.Stock,
                                ct);

                        if (success)
                        {
                            updatedCount++;
                        }
                        else
                        {
                            failedCount++;
                            _logger.LogWarning("Failed to update stock for product with Code: {Code}", stock.Index);
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++; _logger.LogError(ex, "Error occurred while updating stock for product with Code: {Code}", stock.Index);
                    }
                }

                _logger.LogInformation("Stock sync completed. Updated: {Updated}, Failed: {Failed}", updatedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing stock from Rolmar.");
            }
        }

        public async Task SyncImagesAsync(CancellationToken ct = default)
        {
            int savedCount = 0;
            int failedCount = 0;

            try
            {
                var products = await _productRepository.GetAllProducts(ct);
                var body = new RolmarImagesRequest();
                var requestUri = $"v1/photo/photo.php?m=getPhotos&lang=pl&wsKey={_rolmarSettings.ApiKey}";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body, ct);
                response.EnsureSuccessStatusCode();

                var rolmarResponseArray =
                    await response.Content.ReadFromJsonAsync<List<RolmarImagesResponse>>(ct);

                if (rolmarResponseArray == null || !rolmarResponseArray.Any())
                {
                    _logger.LogWarning("No images found in Rolmar response.");
                    return;
                }

                var rolmarResponse = rolmarResponseArray[0];

                string baseDirectory = @"C:\Program Files (x86)\Api Sync Services\RolmarImages";

                Directory.CreateDirectory(baseDirectory);

                foreach (var item in rolmarResponse.PhotoItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Url) || string.IsNullOrWhiteSpace(item.Index))
                        continue;

                    if (!products.Any(p => p.Code == item.Index))
                        continue;

                    try
                    {
                        byte[] imgBytes = await _httpClient.GetByteArrayAsync(item.Url, ct);

                        string extension = Path.GetExtension(item.Url);
                        if (string.IsNullOrWhiteSpace(extension))
                            extension = ".jpg";

                        int counter = 1;
                        string filePath;

                        do
                        {
                            string safeIndex = item.Index.Replace("/", "_").Replace("\\", "_");
                            string fileName = $"{safeIndex}_{counter}{extension}";
                            filePath = Path.Combine(baseDirectory, fileName);
                            counter++;
                        }
                        while (File.Exists(filePath));

                        await File.WriteAllBytesAsync(filePath, imgBytes, ct);
                        savedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        _logger.LogWarning(ex, "Failed to save image for Index: {Index}, Url: {Url}", item.Index, item.Url);
                    }
                }

                _logger.LogInformation("Image sync completed. Saved: {Saved}, Failed: {Failed}", savedCount, failedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing images from Rolmar.");
            }
        }

        private static RolmarProduct MapToRolmarProduct(ProductResult product)
        {
            var priceNet = decimal.TryParse(product.Price, out var pn) ? pn : 0m;
            var weight = float.TryParse(product.Weight, out var w) ? w : 0f;
            var package = decimal.TryParse(product.ErpPackage, out var pkg) ? pkg : 0m;

            return new RolmarProduct
            {
                Code = product.ProductIndex,
                Name = product.Name,
                Description = product.Description,
                Ean = product.Ean,
                Weight = weight,
                Fits = product.Fits,
                Substitutes = product.Substitutes,
                Unit = product.Unit,
                CurrencyPrice = product.Currency,
                PriceNet = priceNet,
                PriceGross = priceNet * 1.23m,
                Package = package,
                Specifications = product.Specifications?.Select(s => new JSAGROSyncServices.Shared.Models.ProductSpecification
                {
                    Name = s.Name,
                    Value = s.Value,
                    UnitName = s.UnitName
                }).ToList(),
                Categories = product.Categories?.Select(c => new RolmarCategory { Name = c }).ToList()
            };
        }
    }
}