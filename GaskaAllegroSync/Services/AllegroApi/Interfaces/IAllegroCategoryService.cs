using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroCategoryService
    {
        Task UpdateAllegroCategories(CancellationToken ct = default);

        Task FetchAndSaveCategoryParameters(CancellationToken ct = default);
    }
}