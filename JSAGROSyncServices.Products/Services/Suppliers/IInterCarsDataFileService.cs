namespace JSAGROSyncServices.Products.Services.Suppliers
{
    /// <summary>Zdjęcia jednego produktu z pliku wymiany, w kolejności ustalonej przez dostawcę.</summary>

    /// <summary>
    /// Pliki CSV wystawiane przez Inter Cars pod <c>data.webapi.intercars.eu</c>. API nie umie
    /// filtrować katalogu do asortymentu rolniczego, a te pliki są już zawężone do produktów AGRO -
    /// to z nich bierzemy listę SKU, które w ogóle wolno synchronizować.
    /// </summary>
    public interface IInterCarsDataFileService
    {
        /// <summary>
        /// Kody SKU dopuszczone do synchronizacji (plik <c>ProductInformation</c>).
        /// <c>null</c> oznacza, że pliku nie udało się pobrać - wtedy nie synchronizujemy nic,
        /// bo bez filtra do bazy trafiłby cały, kilkumilionowy katalog Inter Cars.
        /// </summary>
        Task<HashSet<string>?> GetAllowedSkusAsync(CancellationToken ct = default);
    }
}
