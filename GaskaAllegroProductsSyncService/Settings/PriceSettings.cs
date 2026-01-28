namespace GaskaAllegroProductsSyncService.Settings
{
    public class PriceSettings
    {
        public decimal OwnMarginPercent { get; set; }
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal MinProductPriceNetForFreeDelivery { get; set; }
        public decimal StandardDeliveryPriceNet { get; set; }
        public decimal BulkyDeliveryPriceNet { get; set; }
        public decimal CustomDeliveryPriceNet { get; set; }
        public decimal DropshippingPriceNet { get; set; }
    }
}
