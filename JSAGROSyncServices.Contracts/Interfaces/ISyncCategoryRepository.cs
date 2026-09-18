namespace JSAGROSyncServices.Contracts.Interfaces
{
    /// <summary>
    /// Kategorie dostawcy skonfigurowane dla poszczególnych kont Allegro.
    /// Dane od dostawcy pobiera tylko jedno konto, więc musi ono znać sumę kategorii wszystkich kont.
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

        /// <summary>
        /// Zapisuje przypisanie produktów do kategorii dostawcy, pod którymi zostały pobrane.
        /// Używane dla dostawców, którzy nie zwracają kategorii razem z produktem (Gąska).
        /// Nadpisywane są wyłącznie przypisania do kategorii z <paramref name="fetchedCategoryIds"/>,
        /// czyli tych pobranych w całości - dzięki temu błąd pobierania jednej kategorii
        /// nie kasuje przypisań produktu do pozostałych.
        /// </summary>
        Task ReplaceProductCategoriesAsync(
            IReadOnlyDictionary<string, HashSet<int>> categoryIdsByProductCode,
            IReadOnlyCollection<int> fetchedCategoryIds,
            CancellationToken ct);
    }
}
