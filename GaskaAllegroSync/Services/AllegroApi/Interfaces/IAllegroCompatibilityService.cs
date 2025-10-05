using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroCompatibilityService
    {
        Task FetchAndSaveCompatibleProducts(CancellationToken ct = default);
    }
}