namespace AllegroGaskaProductsSyncService.Settings
{
    public class AppSettings
    {
        public string CategoriesId { get; set; } = string.Empty;
        public int MinProductStock { get; set; }
        public decimal MinProductPrice { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public decimal OwnMarginPercent { get; set; }
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal AddPLNToBulkyProducts { get; set; }
        public decimal AddPLNToCustomProducts { get; set; }

        public string AllegroDeliveryName { get; set; } = string.Empty;
        public string AllegroHandlingTime { get; set; } = string.Empty;
        public string AllegroHandlingTimeCustomProducts { get; set; } = string.Empty;
        public string AllegroSafetyMeasures { get; set; } = string.Empty;
        public string AllegroWarranty { get; set; } = string.Empty;
        public string AllegroReturnPolicy { get; set; } = string.Empty;
        public string AllegroImpliedWarranty { get; set; } = string.Empty;
        public string AllegroResponsiblePerson { get; set; } = string.Empty;
        public string AllegroResponsibleProducer { get; set; } = string.Empty;
    }
}