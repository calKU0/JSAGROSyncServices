namespace AllegroGaskaOrdersSyncService.Settings
{
    public class AppSettings
    {
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public string AllegroDeliveryName { get; set; } = string.Empty;
        public int OfferProcessingDelayMinutes { get; set; }
        public string NotificationsEmail { get; set; } = string.Empty;
    }
}