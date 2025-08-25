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

namespace JSAGROAllegroSync.Data
{
    public interface IProductRepository
    {
        Task UpsertProducts(List<ApiProducts> apiProducts, HashSet<int> fetchedProductIds);

        Task<int> ArchiveProductsNotIn(HashSet<int> fetchedProductIds);

        Task<List<Product>> GetProductsForDetailUpdate(int limit);

        Task<List<ProductImage>> GetImagesForImport(CancellationToken ct);

        Task UpdateProductDetails(Product product, ApiProduct updatedProduct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task<bool> UpdateProductAllegroImage(int imageId, string imageUrl, DateTime expiresAt, CancellationToken ct);

        Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct);

        Task<List<Product>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<Product>> GetProductsToUpdateParameters(CancellationToken ct);

        Task<List<int>> GetDefaultCategories(CancellationToken ct);

        Task<List<Product>> GetProductsToUpload(CancellationToken ct);

        Task<Product> GetByIdAsync(int id, CancellationToken ct);

        Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct);

        Task SetDefaultCategoryAsync(int productId, int categoryId, CancellationToken ct);

        Task SaveProductParametersAsync(List<ProductParameter> parameters, CancellationToken ct);

        Task<List<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct);

        Task SaveCompatibleProductsAsync(IEnumerable<CompatibleProduct> products, CancellationToken ct = default);

        Task SaveCategoryTreeAsync(CategoryDto category, CancellationToken ct);

        Task<List<CompatibleProduct>> GetCompatibilityList(CancellationToken ct);

        Task<List<AllegroCategory>> GetAllegroCategories(CancellationToken ct);

        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task<List<Product>> GetProductsToUpdateOffer(string apiDeliveryName, CancellationToken ct);
    }
}