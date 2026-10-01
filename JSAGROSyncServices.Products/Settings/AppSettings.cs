using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Settings;
using System.Globalization;

namespace JSAGROSyncServices.Products.Settings
{
    public class AppSettings
    {
        /// <summary>Kategorie dostawcy (Gąska) - id kategorii z API dostawcy.</summary>
        public List<int> CategoriesId { get; set; } = new List<int>();

        /// <summary>Kategorie dostawcy (Rolmar) - początki ścieżek kategorii przychodzących z produktem.</summary>
        public List<string> CategoriesName { get; set; } = new List<string>();

        /// <summary>Kategorie dostawcy (Inter Cars) - identyfikatory węzłów katalogu, np. "SalesClassificationNode_5011213".</summary>
        public List<string> CategoriesKey { get; set; } = new List<string>();

        /// <summary>
        /// Kategorie skonfigurowane dla dostawcy obsługiwanego przez serwis. Każdy dostawca
        /// identyfikuje kategorie inaczej (Gąska - liczbowe id, Rolmar - ścieżka, Inter Cars - klucz węzła),
        /// ale dalej w serwisie i w bazie są to po prostu klucze kategorii.
        /// </summary>
        public List<string> GetConfiguredCategories(IntegrationCompany company)
        {
            var values = company switch
            {
                IntegrationCompany.Rolmar => CategoriesName,
                IntegrationCompany.InterCars => CategoriesKey,
                _ => CategoriesId.Where(id => id != 0).Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList()
            };

            return values
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Po ilu dniach nieobecności w danych dostawcy uznać produkt za wycofany. Produkt archiwalny
        /// nie trafia na Allegro, a jego oferta jest kończona. Karencja chroni przed zakończeniem ofert
        /// całego asortymentu przez jedno nieudane pobranie - produkt musi zniknąć z kilku pobrań z rzędu.
        /// </summary>
        public int ArchiveAfterDaysMissing { get; set; } = 3;

        /// <summary>
        /// Ile zapytań do Allegro wysyłać równolegle. Allegro ogranicza nie tylko liczbę zapytań
        /// na minutę, ale też liczbę równoległych wywołań w imieniu jednego użytkownika
        /// (algorytm leaky bucket) - nadmiar wraca jako 429. Większa wartość nie przyspiesza cyklu,
        /// bo odrzucone zapytania i tak trzeba powtórzyć z narastającym odstępem.
        /// </summary>
        public int AllegroParallelism { get; set; } = 8;

        public int MinProductStock { get; set; }
        public decimal MinProductPriceNet { get; set; }
        public decimal BundleProductsUnderPriceNet { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public string PriceDropAlertEmail { get; set; } = string.Empty;

        public List<DeliverySettings> Deliveries { get; set; } = new List<DeliverySettings>();

        /// <summary>Cenniki dostawy, dla których nie aktualizujemy ceny ani dostawy.</summary>
        /// <summary>
        /// Cenniki dostawy zarządzane ręcznie na Allegro. Dla ofert z tych cenników serwis
        /// pilnuje tylko stanu i opisu - nie wysyła ceny, cennika dostawy ani czasu realizacji.
        /// </summary>
        public List<string> DeliveriesWithoutPriceUpdate { get; set; } = new List<string>();

        /// <summary>Wszystkie cenniki obsługiwane przez serwis - tylko ofert z tych cenników wolno dotykać.</summary>
        public IReadOnlyList<string> ManagedDeliveryNames =>
            Deliveries
                .Select(d => d.DeliveryName)
                .Concat(DeliveriesWithoutPriceUpdate)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
