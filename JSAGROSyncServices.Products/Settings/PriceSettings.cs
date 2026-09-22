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

        /// <summary>Produkt jest ponadgabarytowy, gdy którykolwiek jego wymiar przekracza ten próg (cm).</summary>
        public decimal OversizeThresholdCm { get; set; } = 150m;

        /// <summary>
        /// Nazwy specyfikacji traktowane jako wymiary produktu. Dostawca nie podaje wymiarów w osobnych
        /// polach, a w specyfikacjach są też np. głębokość zanurzenia pompy czy długość kabla -
        /// dlatego liczą się tylko nazwy z tej listy. Pusta lista = <see cref="Helpers.ProductDimensions.DefaultDimensionNames"/>.
        /// </summary>
        public List<string> OversizeDimensionNames { get; set; } = new List<string>();
    }

    public class MarginRange
    {
        public decimal Min { get; set; }
        public decimal Max { get; set; }
        public decimal Margin { get; set; }
    }
}
