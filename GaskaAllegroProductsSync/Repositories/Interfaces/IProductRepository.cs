using GaskaAllegroProductsSync.DTOs;
using GaskaAllegroProductsSync.Models;
using GaskaAllegroProductsSync.Models.Product;

namespace GaskaAllegroProductsSync.Repositories.Interfaces
{
    public interface IProductRepository
    {
        Task<Product> GetByIdAsync(int id, CancellationToken ct);

        Task<List<Product>> GetProductsForDetailUpdate(int limit, CancellationToken ct);

        Task<List<Product>> GetProductsToUpload(CancellationToken ct);

        Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds, CancellationToken ct);

        Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds, CancellationToken ct);

        Task UpdateProductDetails(int productId, ApiProduct updatedProduct, CancellationToken ct);

        Task<List<Product>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<Product>> GetProductsToUpdateParameters(CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task UpdateProductAllegroCategory(string productCode, string categoryId, CancellationToken ct);

        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);

        Task UpdateParameter(int id, int parameterId, string value, CancellationToken ct);
    }
}