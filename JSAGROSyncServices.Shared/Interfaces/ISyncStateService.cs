namespace JSAGROSyncServices.Shared.Interfaces
{
    public interface ISyncStateService
    {
        Task<string?> GetLastCategoriesNameAsync();

        Task SetLastCategoriesNameAsync(string categoriesName);
    }
}