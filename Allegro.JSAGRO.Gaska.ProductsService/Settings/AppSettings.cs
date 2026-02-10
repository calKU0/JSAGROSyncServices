using JSAGROSyncServices.Shared.Settings;

namespace Allegro.JSAGRO.Gaska.ProductsService.Settings
{
    public class AppSettings
    {
        public string CategoriesId { get; set; } = string.Empty;
        public int MinProductStock { get; set; }
        public decimal MinProductPriceNet { get; set; }
        public decimal BundleProductsUnderPriceNet { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public List<DeliverySettings> Deliveries { get; set; } = new List<DeliverySettings>();
    }
}