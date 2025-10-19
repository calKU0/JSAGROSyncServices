using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.GaskaApi.Interfaces
{
    public interface IGaskaApiService
    {
        Task SyncProducts(CancellationToken ct = default);

        Task SyncProductDetails(CancellationToken ct = default);
    }
}