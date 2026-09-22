namespace JSAGROSyncServices.Contracts.Models
{
    /// <summary>
    /// Węzeł drzewa kategorii dostawcy. Klucz jest stabilnym identyfikatorem po stronie dostawcy:
    /// dla Gąski to id kategorii z API, dla Rolmara - znormalizowana ścieżka (Rolmar nie ma id kategorii).
    /// </summary>
    public sealed record SupplierCategoryNode(string SourceKey, string? ParentSourceKey, string Name);
}
