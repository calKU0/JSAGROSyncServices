using JSAGROSyncServices.Contracts.Settings;

namespace JSAGROSyncServices.Products.Settings
{
    public class AppSettings
    {
        /// <summary>Kategorie dostawcy (Gąska) - id kategorii z API dostawcy.</summary>
        public List<int> CategoriesId { get; set; } = new List<int>();

        /// <summary>Kategorie dostawcy (Rolmar) - początki ścieżek kategorii przychodzących z produktem.</summary>
        public List<string> CategoriesName { get; set; } = new List<string>();

        public int MinProductStock { get; set; }
        public decimal MinProductPriceNet { get; set; }
        public decimal BundleProductsUnderPriceNet { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public string PriceDropAlertEmail { get; set; } = string.Empty;

        public List<DeliverySettings> Deliveries { get; set; } = new List<DeliverySettings>();

        /// <summary>Cenniki dostawy, dla których nie aktualizujemy ceny ani dostawy.</summary>
        /// <summary>
        /// Wyłącznik wysyłki zdjęć do Allegro. Przydatny, gdy dostawca zwraca uszkodzone
        /// lub niedostępne zdjęcia - oferty są wtedy pomijane zamiast psuć galerię na Allegro.
        /// </summary>
        public bool UploadImagesToAllegro { get; set; } = true;

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
