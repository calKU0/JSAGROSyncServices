namespace Allegro.JSAGRO.Gaska.ProductsService.Settings
{
    public class PriceSettings
    {
        public List<MarginRange> MarginRanges { get; set; } = new();
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal BulkyDeliveryPriceNet { get; set; }
        public decimal CustomDeliveryPriceNet { get; set; }
        public decimal DropshippingPriceNet { get; set; }
    }

    public class MarginRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Margin { get; set; }
    }
}
