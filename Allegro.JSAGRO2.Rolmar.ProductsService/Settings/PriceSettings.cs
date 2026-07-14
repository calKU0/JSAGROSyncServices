namespace Allegro.JSAGRO2.Rolmar.ProductsService.Settings
{
    public class PriceSettings
    {
        public List<MarginRange> MarginRanges { get; set; } = new();
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal MaxPriceDropPercent { get; set; } = 30m;
    }

    public class MarginRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Margin { get; set; }
    }
}
