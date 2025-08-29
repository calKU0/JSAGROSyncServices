using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroCompatibilityService
    {
        Task FetchAndSaveCompatibleProducts(CancellationToken ct = default);
    }
}