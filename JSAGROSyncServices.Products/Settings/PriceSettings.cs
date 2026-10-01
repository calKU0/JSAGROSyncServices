namespace JSAGROSyncServices.Products.Settings
{
    public class PriceSettings
    {
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal BulkyDeliveryPriceNet { get; set; }
        public decimal CustomDeliveryPriceNet { get; set; }
        public decimal DropshippingPriceNet { get; set; }

        /// <summary>Maksymalny spadek ceny (%), przy którym jeszcze aktualizujemy cenę. 0 = brak ograniczenia.</summary>
        public decimal MaxPriceDropPercent { get; set; }

        public List<MarginRange> MarginRanges { get; set; } = new List<MarginRange>();

        /// <summary>Dopłata (PLN) do ceny oferty produktu ponadgabarytowego. 0 = bez dopłaty.</summary>
        public decimal OversizeSurcharge { get; set; }

        /// <summary>
        /// Produkt jest ponadgabarytowy, gdy którykolwiek jego wymiar przekracza ten próg (cm).
        /// Wymiary czyta <see cref="Helpers.ProductDimensions"/> - ten sam parser, co dobór cennika dostawy.
        /// 0 wyłącza dopłatę.
        /// </summary>
        public decimal OversizeThresholdCm { get; set; } = 150m;
    }

    public class MarginRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Margin { get; set; }
    }
}
