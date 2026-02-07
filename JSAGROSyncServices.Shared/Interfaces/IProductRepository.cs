using JSAGROSyncServices.Shared.Models;

namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IProductRepository
    {
        public Task<bool> UpsertProductAsync(RolmarProduct product, CancellationToken ct);

        public Task<bool> UpdateProductStockAsync(string productCode, int stock, CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, CancellationToken ct);

        Task<List<RolmarProduct>> GetAllProducts(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task UpdateProductAllegroCategory(string code, string categoryId, CancellationToken ct);

        Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct);

        Task UpdateProductAllegroId(int productId, string allegroProductId, string allegroCategoryId, CancellationToken ct);
    }
}