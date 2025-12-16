using RolmarAllegroProductsSyncService.DTOs;
using RolmarAllegroProductsSyncService.DTOs.Rolmar;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;
using RolmarAllegroProductsSyncService.Services.Interfaces;
using System.Net.Http.Json;

namespace RolmarAllegroProductsSyncService.Services.Rolmar
{
    public class RolmarSyncService : IRolmarSyncService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RolmarSyncService> _logger;
        private readonly IProductRepository _productRepository;

        public RolmarSyncService(HttpClient httpClient, ILogger<RolmarSyncService> logger, IProductRepository productRepository)
        {
            _httpClient = httpClient;
            _logger = logger;
            _productRepository = productRepository;
        }

        public async Task SyncProductsAsync(CancellationToken ct = default)
        {
            try
            {
                var body = new RolmarProductsRequest();
                var requestUri = "v1/product/products.php?m=getProducts&lang=pl";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body);
                response.EnsureSuccessStatusCode();
                var rolmarResponse = await response.Content.ReadFromJsonAsync<RolmarProductReponse>();

                if (rolmarResponse == null || rolmarResponse.Products == null)
                {
                    _logger.LogWarning("No products found in Rolmar response.");
                    return;
                }

                foreach (var product in rolmarResponse.Products)
                {
                    try
                    {
                        bool success = await _productRepository.UpsertProductAsync(product, ct);
                        if (!success)
                        {
                            _logger.LogWarning($"Failed to upsert product with Code: {product.ProductIndex}");
                        }
                        _logger.LogInformation($"Upserted product with Code: {product.ProductIndex} successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error occurred while upserting product with Code: {product.ProductIndex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing products from Rolmar.");
            }
        }

        public async Task SyncStockAsync(CancellationToken ct = default)
        {
            try
            {
                var body = new RolmarStockRequest();
                var requestUri = "v1/stock/stock.php?m=getStock&lang=pl";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body);
                response.EnsureSuccessStatusCode();
                var rolmarResponse = await response.Content.ReadFromJsonAsync<RolmarStockResponse>();

                if (rolmarResponse == null || rolmarResponse.StockItems == null)
                {
                    _logger.LogWarning("No products found in Rolmar response.");
                    return;
                }

                foreach (var stock in rolmarResponse.StockItems)
                {
                    try
                    {
                        bool success = await _productRepository.UpdateProductStockAsync(stock.Index, stock.Stock, ct);
                        if (!success)
                        {
                            _logger.LogWarning($"Failed to update stock for product with Code: {stock.Index}");
                        }
                        _logger.LogInformation($"Updated stock for product with Code: {stock.Index} successfully.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error occurred while updating stock for product with Code: {stock.Index}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing stock from Rolmar.");
            }
        }

        public async Task SyncImagesAsync(CancellationToken ct = default)
        {
            try
            {
                var body = new RolmarImagesRequest();
                var requestUri = "v1/photo/photo.php?m=getPhotos&lang=pl";

                var response = await _httpClient.PostAsJsonAsync(requestUri, body);
                response.EnsureSuccessStatusCode();

                var rolmarResponse = await response.Content.ReadFromJsonAsync<RolmarImagesResponse>();

                if (rolmarResponse == null || rolmarResponse.PhotoItems == null)
                {
                    _logger.LogWarning("No products found in Rolmar response.");
                    return;
                }

                string saveDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images");

                if (!Directory.Exists(saveDirectory))
                    Directory.CreateDirectory(saveDirectory);

                foreach (var item in rolmarResponse.PhotoItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Url))
                        continue;

                    byte[] imgBytes = await _httpClient.GetByteArrayAsync(item.Url);

                    string extension = Path.GetExtension(item.Url);

                    // fallback if URL has no extension
                    if (string.IsNullOrWhiteSpace(extension))
                        extension = ".jpg";

                    int counter = 1;
                    string filePath;

                    // Find the next available file name
                    do
                    {
                        string fileName = $"{item.Index}_{counter}{extension}";
                        filePath = Path.Combine(saveDirectory, fileName);
                        counter++;
                    }
                    while (File.Exists(filePath));

                    // Save file
                    await File.WriteAllBytesAsync(filePath, imgBytes);
                    _logger.LogInformation($"Saved image for Index: {item.Index} at {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while syncing images from Rolmar.");
            }
        }
    }
}