using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Services;

namespace Allegro.JSAGRO2.Rolmar.ProductsService.Services.Allegro
{
    public class AllegroProductService : IAllegroProductService
    {
        private readonly ILogger<AllegroProductService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly IProductRepository _productRepository;

        public AllegroProductService(ILogger<AllegroProductService> logger, AllegroApiClient apiClient, IProductRepository productRepository)
        {
            _logger = logger;
            _apiClient = apiClient;
            _productRepository = productRepository;
        }

        public async Task SearchProducts(CancellationToken ct = default)
        {
            var products = await _productRepository.GetNotExistingProductsInAllegro(ct);

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
                        var allegroProduct = await FindAllegroProduct(product.Ean, token);

                        if (allegroProduct.ProductId == null)
                        {
                            allegroProduct = await FindAllegroProduct(product.Code, token);
                        }

                        if (allegroProduct.ProductId == null)
                        {
                            _logger.LogInformation("Product not found on Allegro. EAN: {Ean}, Code: {Code}", product.Ean, product.Code);
                            return;
                        }

                        await _productRepository.UpdateProductAllegroId(product.Id, allegroProduct.ProductId, allegroProduct.CategoryId, token);
                        _logger.LogInformation("Product {Code} updated with Allegro ID {AllegroId} and Category ID {CategoryId}", product.Code, allegroProduct.ProductId, allegroProduct.CategoryId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed processing product {ProductId}", product.Id);
                    }
                });
        }

        private async Task<(string? ProductId, string? CategoryId)> FindAllegroProduct(string phrase, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(phrase))
                return (null, null);

            var result = await _apiClient.GetAsync<SearchProdustsResponse>($"/sale/products?phrase={Uri.EscapeDataString(phrase)}", ct);

            var productId = result?.Products?.FirstOrDefault()?.Id;
            var categoryId = result?.Products?.FirstOrDefault()?.Category?.Id;

            return (productId, categoryId);
        }
    }
}