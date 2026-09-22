using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    /// <summary>
    /// Drzewo kategorii dostawcy i przypisania produktów do kategorii.
    /// Implementacja działa w kontekście jednego dostawcy.
    /// </summary>
    public interface ISupplierCategoryRepository
    {
        /// <summary>Dodaje brakujące węzły, aktualizuje nazwy i rodziców, przelicza ścieżki.</summary>
        Task UpsertNodesAsync(IEnumerable<SupplierCategoryNode> nodes, CancellationToken ct);

        /// <summary>
        /// Zastępuje przypisania kategorii podanych produktów (klucz = kod produktu).
        /// Produkty spoza słownika pozostają nietknięte.
        /// </summary>
        Task ReplaceProductCategoriesAsync(IReadOnlyDictionary<string, List<string>> categoryKeysByProductCode, CancellationToken ct);
    }
}
