namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface IAllegroCategoryService
    {
        Task UpdateAllegroCategories(CancellationToken ct = default);

        Task FetchAndSaveCategoryParameters(CancellationToken ct = default);
    }
}