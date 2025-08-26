using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.AllegroApiResponses;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Models.Product;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories.Interfaces
{
    public interface IProductRepository
    {
        Task<Product> GetByIdAsync(int id, CancellationToken ct);

        Task<List<Product>> GetProductsForDetailUpdate(int limit, CancellationToken ct);

        Task<List<Product>> GetProductsToUpload(CancellationToken ct);

        Task<List<Product>> GetProductsToUpdateOffer(string apiDeliveryName, CancellationToken ct);

        Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds, CancellationToken ct);

        Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct);

        Task UpdateProductDetails(Product product, ApiProduct updatedProduct, CancellationToken ct);

        Task<List<Product>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<Product>> GetProductsToUpdateParameters(CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task SaveCompatibleProductsAsync(IEnumerable<CompatibleProduct> products, CancellationToken ct);

        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);
    }
}