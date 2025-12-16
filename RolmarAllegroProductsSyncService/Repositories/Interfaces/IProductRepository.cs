using RolmarAllegroProductsSyncService.DTOs;
using RolmarAllegroProductsSyncService.Models;

namespace RolmarAllegroProductsSyncService.Repositories.Interfaces
{
    public interface IProductRepository
    {
        public Task<bool> UpsertProductAsync(ProductResult product, CancellationToken ct);

        public Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task UpdateProductAllegroCategory(string code, string categoryId, CancellationToken ct);
    }
}