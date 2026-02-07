namespace Allegro.JSAGRO.Rolmar.ProductsService.Settings
{
    public class PriceSettings
    {
        public decimal OwnMarginPercent { get; set; }
        public decimal OwnMarginPercentUnder10PLN { get; set; }
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
    }
}
