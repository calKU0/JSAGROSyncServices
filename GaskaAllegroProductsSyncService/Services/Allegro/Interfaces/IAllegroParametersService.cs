namespace AllegroGaskaProductsSyncService.Services.Allegro.Interfaces
{
    public interface IAllegroParametersService
    {
        Task UpdateParameters(CancellationToken ct = default);
    }
}