namespace GaskaAllegroProductsSync.Services.Allegro.Interfaces
{
    public interface IAllegroCategoryService
    {
        Task UpdateAllegroCategories(CancellationToken ct = default);

        Task FetchAndSaveCategoryParameters(CancellationToken ct = default);
    }
}