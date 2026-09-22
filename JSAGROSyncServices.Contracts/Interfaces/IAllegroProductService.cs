namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IAllegroProductService
    {
        Task SearchProducts(CancellationToken ct = default);

        /// <summary>
        /// Szuka produktu w katalogu Allegro: najpierw po EAN, potem po kodzie, na końcu po nazwie.
        /// Zwraca id produktu i jego kategorię albo (null, null), gdy nic nie pasuje.
        /// </summary>
        Task<(string? ProductId, string? CategoryId)> FindCatalogProduct(Models.RolmarProduct product, CancellationToken ct);
    }
}
