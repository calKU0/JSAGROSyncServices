namespace GaskaAllegroSyncService.Services.Allegro.Interfaces
{
    public interface IAllegroParametersService
    {
        Task UpdateParameters(CancellationToken ct = default);
    }
}