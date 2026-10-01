namespace JSAGROSyncServices.Contracts.Interfaces
{
    /// <summary>
    /// Produkt z katalogu Allegro dopasowany do produktu dostawcy. <c>ProductId</c> równy <c>null</c>
    /// oznacza brak dopasowania.
    /// </summary>
    public sealed record CatalogProductMatch(string? ProductId, string? CategoryId, string? Name)
    {
        public static readonly CatalogProductMatch None = new(null, null, null);
    }

    public interface IAllegroProductService
    {
        Task SearchProducts(CancellationToken ct = default);

        /// <summary>
        /// Szuka produktu w katalogu Allegro: najpierw po EAN, potem po numerze katalogowym.
        /// Zwraca id produktu i jego kategorię tylko wtedy, gdy numer katalogowy albo EAN znalezionego
        /// produktu zgadza się z naszym - inaczej (null, null). Wyszukiwarka Allegro dopasowuje
        /// przybliżenie i bez tego sprawdzenia podpinała produkty niemające z naszym nic wspólnego.
        /// </summary>
        Task<CatalogProductMatch> FindCatalogProduct(Models.RolmarProduct product, CancellationToken ct);
    }
}
