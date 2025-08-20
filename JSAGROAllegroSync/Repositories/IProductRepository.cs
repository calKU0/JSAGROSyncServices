using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.Models;
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

        Task UpdateProductDetails(Product product, ApiProduct updatedProduct);

        Task UpdateProductAllegroCategory(int productId, int categoryId);

        Task<int?> GetMostCommonDefaultAllegroCategoryAsync(int productId, CancellationToken ct);

        Task<List<ProductDto>> GetProductsToUpload();
    }
}