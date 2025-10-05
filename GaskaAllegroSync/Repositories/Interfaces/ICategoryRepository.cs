using GaskaAllegroSync.DTOs.AllegroApiResponses;
using GaskaAllegroSync.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories.Interfaces
{
    public interface ICategoryRepository
    {
        Task SaveCategoryTreeAsync(CategoryDto category, CancellationToken ct);

        Task<List<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct);

        Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct);

        Task<List<int>> GetDefaultCategories(CancellationToken ct);

        Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct);

        Task<List<AllegroCategory>> GetAllegroCategories(CancellationToken ct);

        Task<List<CompatibleProduct>> GetCompatibilityList(CancellationToken ct);
    }
}