using JSAGROSyncServices.Shared.DTOs.Allegro;
using JSAGROSyncServices.Shared.Models;
using CategoryParameter = Allegro.JSAGRO.Gaska.ProductsService.Models.CategoryParameter;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories.Interfaces
{
    public interface ICategoryRepository
    {
        Task SaveCategoryTreeAsync(CategoryDto category, CancellationToken ct);

        Task<IEnumerable<CategoryParameter>> GetCategoryParametersAsync(int categoryId, CancellationToken ct);

        Task SaveCategoryParametersAsync(IEnumerable<CategoryParameter> parameters, CancellationToken ct);

        Task<IEnumerable<int>> GetDefaultCategories(CancellationToken ct);

        Task<int?> GetMostCommonDefaultAllegroCategory(int productId, CancellationToken ct);

        Task<IEnumerable<AllegroCategory>> GetAllegroCategories(CancellationToken ct);
    }
}