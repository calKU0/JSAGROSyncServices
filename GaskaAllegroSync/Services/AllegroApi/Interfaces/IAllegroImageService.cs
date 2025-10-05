using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroImageService
    {
        Task ImportImages(CancellationToken ct = default);
    }
}