using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IProductRepository
    {
        Task<List<int>> GetProductsForDetailUpdate(int limit, CancellationToken ct);

        /// <summary>
        /// Kolejka produktów do pobrania szczegółów, identyfikowanych kodem (dostawcy z alfanumerycznym SKU).
        /// Najpierw produkty bez szczegółów, potem te odświeżane dawniej niż <paramref name="refreshAfterDays"/> temu.
        /// </summary>
        /// <param name="limit">Górny limit pozycji; wartość &lt;= 0 oznacza brak limitu.</param>
        /// <param name="refreshAfterDays">Po ilu dniach odświeżać szczegóły produktu, który już je ma.</param>
        /// <param name="ct">Token anulowania.</param>
        Task<List<string>> GetProductCodesForDetailUpdate(int limit, int refreshAfterDays, CancellationToken ct);

        /// <summary>Odnotowuje pobranie szczegółów - bez tego te same produkty wracałyby w każdym cyklu.</summary>
        Task MarkDetailsFetched(IEnumerable<string> codes, CancellationToken ct);

        /// <summary>
        /// Odnotowuje, że dostawca nadal ma te produkty w ofercie. Wołane po każdym pobraniu listy
        /// produktów, na komplecie kodów z tego pobrania. Produkt, który wrócił, przestaje być archiwalny.
        /// </summary>
        Task MarkProductsSeenAsync(IEnumerable<string> codes, CancellationToken ct);

        /// <summary>
        /// Oznacza jako archiwalne produkty, których dostawca nie oddał od <paramref name="graceDays"/> dni.
        /// Karencja chroni przed zakończeniem ofert całego katalogu przez jedno nieudane pobranie.
        /// </summary>
        /// <param name="categories">
        /// Kategorie, z których serwis faktycznie pobiera produkty (suma wszystkich kont Allegro).
        /// Produkt spoza nich nie jest pobierany wcale, więc jego brak nic nie znaczy - jego ofertę
        /// i tak kończy filtr kategorii. Pusta lista wyłącza to zawężenie.
        /// </param>
        /// <param name="graceDays">Po ilu dniach nieobecności w danych dostawcy uznać produkt za wycofany.</param>
        /// <param name="ct">Token anulowania.</param>
        /// <returns>Liczba nowo zarchiwizowanych produktów.</returns>
        Task<int> ArchiveMissingProductsAsync(int graceDays, IReadOnlyCollection<string> categories, CancellationToken ct);

        /// <summary>
        /// Zapisane już dane produktów, po kodzie. Dostawcy dzielący produkt na kilka wywołań API
        /// przepisują stąd pola nieobecne w bieżącej odpowiedzi, żeby zapis ich nie wyczyścił.
        /// </summary>
        Task<Dictionary<string, RolmarProduct>> GetProductsByCodesAsync(IEnumerable<string> codes, CancellationToken ct);

        Task<bool> UpsertProductAsync(RolmarProduct product, CancellationToken ct);
        Task UpsertProductsBatchAsync(List<RolmarProduct> product, CancellationToken ct);
        Task<RolmarProduct?> GetProductByIntegrationIdAsync(int integrationId, CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpload(int minProductStock, decimal minProductPrice, CancellationToken ct);

        Task<int> UpdateProductStockBatchAsync(IReadOnlyCollection<ProductStockUpdate> items, CancellationToken ct);

        Task<List<RolmarProduct>> GetAllProducts(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsWithoutDefaultCategory(CancellationToken ct);

        Task<List<RolmarProduct>> GetProductsToUpdateParameters(CancellationToken ct);

        /// <summary>Kategorie Allegro, dla których liczymy parametry produktu: domyślna i kategorie jego ofert.</summary>
        Task<Dictionary<int, List<int>>> GetParameterTargetCategories(CancellationToken ct);

        /// <summary>Odnotowuje wyliczenie parametrów - produkt wróci dopiero po zmianie danych.</summary>
        Task SetParametersChecked(IEnumerable<int> productIds, CancellationToken ct);

        /// <summary>
        /// Przy kolejnej aktualizacji wysyła parametry produktu razem z id produktu z katalogu.
        /// Zwraca true, gdy tryb był już włączony (samo przełączenie nic nie zmieni).
        /// </summary>
        Task<bool> RequestCatalogParameters(int productId, CancellationToken ct);

        /// <summary>Allegro odłączyło produkt z katalogu od oferty - zapamiętuje id i czyści je przy produkcie i ofercie.</summary>
        Task RejectAllegroProduct(int productId, string? offerId, CancellationToken ct);

        Task<HashSet<string>> GetRejectedAllegroIds(int productId, CancellationToken ct);

        /// <summary>Czy produkt z katalogu Allegro jest już podpięty pod inny nasz produkt (dowolnego dostawcy).</summary>
        Task<bool> IsAllegroIdTakenByOther(string allegroId, int productId, CancellationToken ct);

        /// <summary>
        /// Odpina produkt z katalogu od produktu i oferty, wyłącza wysyłanie parametrów produktu
        /// i kasuje znacznik wyszukiwania - kolejny cykl od razu szuka innego produktu z katalogu.
        /// </summary>
        /// <param name="productId">Produkt dostawcy.</param>
        /// <param name="offerId">Oferta, z której zdjąć id produktu (opcjonalnie).</param>
        /// <param name="reject">Zapamiętaj odpinane id, żeby wyszukiwanie nie podpięło go ponownie.</param>
        /// <param name="ct">Token anulowania.</param>
        Task UnlinkAllegroProduct(int productId, string? offerId, bool reject, CancellationToken ct);

        Task UpdateProductAllegroCategory(int productId, int categoryId, CancellationToken ct);

        Task UpdateProductAllegroCategory(string code, string categoryId, CancellationToken ct);

        Task<List<RolmarProduct>> GetNotExistingProductsInAllegro(CancellationToken ct);
        Task UpdateCompatibilitySet(int productId, bool value, CancellationToken ct);

        Task UpdateProductAllegroId(int productId, string? allegroProductId, string allegroCategoryId, string? allegroName, CancellationToken ct);

        /// <summary>
        /// Zapamietuje, ze produkty byly juz szukane w katalogu Allegro. Bez tego
        /// te same nieznalezione produkty sa odpytywane w kazdym cyklu.
        /// </summary>
        Task MarkAllegroSearched(IEnumerable<int> productIds, CancellationToken ct);
    }
}