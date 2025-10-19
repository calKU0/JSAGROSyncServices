namespace GaskaAllegroProductsSync.Services.Allegro.Interfaces
{
    public interface IAllegroImageService
    {
        Task ImportImages(CancellationToken ct = default);
    }
}