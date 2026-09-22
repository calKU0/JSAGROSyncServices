using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Services;

using System.Collections.Concurrent;
using System.Diagnostics;

namespace JSAGROSyncServices.Products.Services.Allegro
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

            if (products.Count == 0)
            {
                _logger.LogInformation("Allegro product search: nothing to look up.");
                return;
            }

            _logger.LogInformation("Allegro product search: {Count} products to look up.", products.Count);

            int found = 0, notFound = 0, failed = 0;

            // Produkty, ktore odpytalismy - niezaleznie od wyniku. Nieznalezionych nie
            // odpytujemy ponownie w kazdym cyklu, tylko po uplywie okresu karencji.
            var searched = new ConcurrentBag<int>();
            var sw = Stopwatch.StartNew();

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
                        var allegroProduct = await FindCatalogProduct(product, token);

                        searched.Add(product.Id);

                        if (allegroProduct.ProductId == null || allegroProduct.CategoryId == null)
                        {
                            Interlocked.Increment(ref notFound);
                            _logger.LogDebug("Product not found on Allegro. EAN: {Ean}, Code: {Code}", product.Ean, product.Code);
                            return;
                        }

                        await _productRepository.UpdateProductAllegroId(product.Id, allegroProduct.ProductId, allegroProduct.CategoryId, token);

                        Interlocked.Increment(ref found);
                        _logger.LogDebug("Product {Code} matched Allegro product {AllegroId} in category {CategoryId}.", product.Code, allegroProduct.ProductId, allegroProduct.CategoryId);
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref failed);
                        _logger.LogError(ex, "Searching Allegro for product {Code} failed.", product.Code);
                    }
                });

            sw.Stop();

            if (!searched.IsEmpty)
                await _productRepository.MarkAllegroSearched(searched, ct);

            _logger.LogInformation(
                "Allegro product search: matched {Found}, not found {NotFound}, failed {Failed} of {Total}. Took {Elapsed}.",
                found, notFound, failed, products.Count, sw.Elapsed);
        }

        public async Task<(string? ProductId, string? CategoryId)> FindCatalogProduct(RolmarProduct product, CancellationToken ct)
        {
            var found = string.IsNullOrWhiteSpace(product.Ean)
                ? (ProductId: null, CategoryId: (string?)null)
                : await FindAllegroProduct(product.Ean, ct);

            if (found.ProductId == null)
                found = await FindAllegroProduct(product.Code, ct);

            if (found.ProductId == null)
                found = await FindAllegroProduct(product.Name, ct);

            return found;
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