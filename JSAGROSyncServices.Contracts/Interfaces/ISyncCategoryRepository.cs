namespace JSAGROSyncServices.Contracts.Interfaces
{
    /// <summary>
    /// Kategorie dostawcy skonfigurowane dla poszczególnych kont Allegro.
    /// Rolmar pobiera do bazy tylko produkty z kategorii skonfigurowanych na dowolnym z kont,
    /// więc konto pobierające musi znać sumę kategorii wszystkich kont.
    /// </summary>
    public interface ISyncCategoryRepository
    {
        /// <summary>
        /// Zapisuje kategorie skonfigurowane dla konta tego serwisu (usuwa te, których już nie ma w konfiguracji).
        /// </summary>
        Task ReplaceAccountCategoriesAsync(IEnumerable<string> categories, CancellationToken ct);

        /// <summary>
        /// Zwraca sumę kategorii wszystkich kont Allegro dla dostawcy obsługiwanego przez ten serwis.
        /// </summary>
        Task<List<string>> GetCompanyCategoriesAsync(CancellationToken ct);
    }
}
