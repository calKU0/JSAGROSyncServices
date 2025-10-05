using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Services.AllegroApi.Interfaces
{
    public interface IAllegroParametersService
    {
        Task UpdateParameters(CancellationToken ct = default);
    }
}