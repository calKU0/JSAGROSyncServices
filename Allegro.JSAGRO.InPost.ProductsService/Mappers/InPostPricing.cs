using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;

namespace Allegro.JSAGRO.InPost.ProductsService.Mappers
{
    /// <summary>
    /// Cena oferty InPost liczona od ceny zakupu, a nie przepisywana z Allegro - cena Allegro
    /// zawiera prowizję Allegro i koszt Allegro Smart, których w InPost nie ma.
    ///
    /// Kolejność jak na Allegro: marża własna od ceny zakupu brutto, dopłaty za gabaryt
    /// i towar niestandardowy, koszt wysyłki, a na końcu prowizja platformy od całej kwoty
    /// (InPost pobiera prowizję od ceny, którą płaci kupujący).
    /// </summary>
    public static class InPostPricing
    {
        public const decimal MinimumPrice = 1.00m;

        public static PriceBreakdown Calculate(RolmarProduct product, InPostPriceSettings settings)
        {
            var purchaseGross = PurchasePriceGross(product);
            var marginPercent = ResolveMargin(settings, purchaseGross);
            var withMargin = purchaseGross * (1 + marginPercent / 100m);

            var vatMultiplier = 1 + settings.VatRate / 100m;

            var deliveryTypeSurcharge = product.DeliveryType switch
            {
                1 => settings.BulkyDeliveryPriceNet * vatMultiplier,
                2 => settings.CustomDeliveryPriceNet * vatMultiplier,
                _ => 0m
            };

            var oversizeSurcharge = settings.OversizeSurcharge > 0
                && ProductDimensions.IsOversized(product, settings.OversizeThresholdCm)
                    ? settings.OversizeSurcharge
                    : 0m;

            var beforeCommission = withMargin + deliveryTypeSurcharge + oversizeSurcharge + settings.ShippingCostGross;
            var final = beforeCommission * (1 + settings.CommissionPercent / 100m);

            final = Math.Max(Math.Round(final, 2, MidpointRounding.AwayFromZero), MinimumPrice);

            return new PriceBreakdown(
                purchaseGross,
                marginPercent,
                Math.Round(withMargin, 2, MidpointRounding.AwayFromZero),
                Math.Round(deliveryTypeSurcharge, 2, MidpointRounding.AwayFromZero),
                oversizeSurcharge,
                settings.ShippingCostGross,
                settings.CommissionPercent,
                final);
        }

        /// <summary>
        /// Cena zakupu brutto. Przy wykrytym zbyt dużym spadku ceny serwis Rolmar trzyma na Allegro
        /// ostatnią zaakceptowaną cenę - tu robimy tak samo, żeby błąd w cenniku dostawcy
        /// nie wystawił towaru za bezcen.
        /// </summary>
        public static decimal PurchasePriceGross(RolmarProduct product) =>
            product.PriceDropDetectedAt != null && product.AcceptedPriceGross is > 0
                ? product.AcceptedPriceGross.Value
                : product.PriceGross;

        private static decimal ResolveMargin(InPostPriceSettings settings, decimal grossPrice)
        {
            if (settings.MarginRanges.Count == 0)
                return 0m;

            return settings.MarginRanges
                .FirstOrDefault(r => grossPrice >= r.Min && grossPrice <= r.Max)?.Margin
                ?? settings.MarginRanges[^1].Margin;
        }
    }

    public record PriceBreakdown(
        decimal PurchaseGross,
        decimal MarginPercent,
        decimal WithMargin,
        decimal DeliveryTypeSurcharge,
        decimal OversizeSurcharge,
        decimal ShippingCost,
        decimal CommissionPercent,
        decimal Final);
}
