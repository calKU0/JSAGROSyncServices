namespace Allegro.JSAGRO2.Gaska.OrdersService.Settings
{
    public class AppSettings
    {
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public string AllegroDeliveryNames { get; set; } = string.Empty;
        public int OfferProcessingDelayMinutes { get; set; }
        public string NotificationsEmail { get; set; } = string.Empty;
    }
}