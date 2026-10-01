namespace JSAGROSyncServices.Products.Services.Suppliers
{
    public interface IInterCarsApiService
    {
        /// <summary>Drzewo kategorii i produkty ze skonfigurowanych kategorii (razem z ceną i stanem).</summary>
        Task SyncProductsAsync(CancellationToken ct = default);

        /// <summary>Stany magazynowe wszystkich produktów Inter Cars w bazie.</summary>
        Task SyncStockAsync(CancellationToken ct = default);

        /// <summary>Szczegóły produktów - waga, wymiary i EAN. Jedno zapytanie na SKU, więc porcjami.</summary>
        Task SyncProductDetailsAsync(CancellationToken ct = default);

        /// <summary>Zdjęcia produktów z pliku wymiany - API katalogu ich nie zwraca.</summary>
        Task SyncImagesAsync(CancellationToken ct = default);

        /// <summary>
        /// Pełne drzewo kategorii do listy wyboru w konfiguratorze. Ponad tysiąc zapytań,
        /// więc krok dzienny - pobieranie produktów korzysta z płytszego drzewa z każdego cyklu.
        /// </summary>
        Task SyncCategoryTreeAsync(CancellationToken ct = default);
    }
}
