namespace Allegro.JSAGRO.Rolmar.ProductsService.Settings
{
    public class AppSettings
    {
        public string CategoriesName { get; set; } = string.Empty;
        public int MinProductStock { get; set; }
        public decimal MinProductPrice { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public List<Delivery> Deliveries { get; set; } = new List<Delivery>();
    }
}