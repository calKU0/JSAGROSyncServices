using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IProductRepository
    {
        Task<List<int>> GetProductsForDetailUpdate(int limit, CancellationToken ct);
        Task<bool> UpsertProductAsync(RolmarProduct product, CancellationToken ct);
        Task UpsertProductsBatchAsync(List<RolmarProduct> product, CancellationToken ct);
        Task<RolmarProduct?> GetProductByIntegrationIdAsync(int integrationId, CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, decimal minProductPrice, CancellationToken ct);

        Task<int> UpdateProductStockBatchAsync(IReadOnlyCollection<ProductStockUpdate> items, CancellationToken ct);

        Task<List<RolmarProduct>> GetAllProducts(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task UpdateProductAllegroCategory(string code, string categoryId, CancellationToken ct);

        Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct);
        Task UpdateCompatibilitySet(int productId, bool value, CancellationToken ct);

        Task UpdateProductAllegroId(int productId, string allegroProductId, string allegroCategoryId, CancellationToken ct);

        /// <summary>
        /// Zapamietuje, ze produkty byly juz szukane w katalogu Allegro. Bez tego
        /// te same nieznalezione produkty sa odpytywane w kazdym cyklu.
        /// </summary>
        Task MarkAllegroSearched(IEnumerable<int> productIds, CancellationToken ct);
    }
}