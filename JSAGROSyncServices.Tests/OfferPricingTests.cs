using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Settings;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Narzuty doliczane do ceny oferty. Te same drabinki obowiązują u wszystkich dostawców,
    /// więc błąd tutaj przekłada się wprost na marżę na każdej ofercie.
    /// </summary>
    public class OfferPricingTests
    {
        private static PriceSettings Settings() => new()
        {
            AllegroMarginUnder5PLN = 0.50m,
            AllegroMarginBetween5and1000PLNPercent = 10m,
            AllegroMarginMoreThan1000PLN = 100m,
            OversizeSurcharge = 22m,
            OversizeThresholdCm = 150m
        };

        // ------------------------------------------------------------ prowizja Allegro

        [Fact]
        public void Below_five_zloty_adds_a_flat_fee()
        {
            Assert.Equal(2.50m, OfferPricing.AddAllegroCommission(2m, Settings()));
        }

        [Fact]
        public void Flat_fee_crossing_five_zloty_switches_to_the_percentage()
        {
            // 4,80 + 0,50 = 5,30 przekracza próg, więc obowiązuje reguła wyższego progu: 4,80 + 10%.
            Assert.Equal(5.28m, OfferPricing.AddAllegroCommission(4.80m, Settings()));
        }

        [Fact]
        public void Between_five_and_thousand_adds_a_percentage()
        {
            Assert.Equal(110m, OfferPricing.AddAllegroCommission(100m, Settings()));
        }

        [Fact]
        public void Percentage_crossing_thousand_switches_to_the_flat_fee()
        {
            // 950 + 10% = 1045 przekracza próg, więc zamiast procentu idzie kwota stała.
            Assert.Equal(1050m, OfferPricing.AddAllegroCommission(950m, Settings()));
        }

        [Fact]
        public void Above_thousand_adds_a_flat_fee()
        {
            Assert.Equal(1600m, OfferPricing.AddAllegroCommission(1500m, Settings()));
        }

        // ------------------------------------------------------------ koszt wysyłki

        [Theory]
        [InlineData(30, 1.99)]
        [InlineData(44.99, 1.99)]
        [InlineData(45, 3.99)]
        [InlineData(64.99, 3.99)]
        [InlineData(65, 5.79)]
        [InlineData(99.99, 5.79)]
        [InlineData(100, 9.09)]
        [InlineData(149.99, 9.09)]
        [InlineData(150, 11.49)]
        [InlineData(10000, 11.49)]
        public void Shipping_cost_follows_the_price_thresholds(decimal price, decimal expected)
        {
            Assert.Equal(expected, OfferPricing.ShippingCost(price));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(15, 1.00)]
        [InlineData(29.99, 1.99)]
        public void Below_the_first_threshold_the_cost_is_proportional(decimal price, decimal expected)
        {
            Assert.Equal(expected, OfferPricing.ShippingCost(price));
        }

        [Theory]
        // Stara implementacja miała dziury między progami ("<= 44.99" i ">= 45"),
        // przez co dla cen w środku nie doliczała wysyłki w ogóle.
        [InlineData(44.995)]
        [InlineData(64.995)]
        [InlineData(99.995)]
        [InlineData(149.995)]
        public void There_is_no_gap_between_shipping_thresholds(decimal price)
        {
            Assert.True(OfferPricing.ShippingCost(price) > 0m);
        }

        [Fact]
        public void Shipping_cost_never_decreases_as_the_price_grows()
        {
            decimal previous = 0m;

            for (var price = 0m; price <= 200m; price += 0.25m)
            {
                var cost = OfferPricing.ShippingCost(price);

                Assert.True(cost >= previous, $"Koszt spadł przy cenie {price}: {previous} -> {cost}.");
                previous = cost;
            }
        }

        // ------------------------------------------------------------ całość

        [Fact]
        public void Finalize_applies_commission_then_shipping()
        {
            // 100 + 10% prowizji = 110, do tego wysyłka z progu 100-149,99.
            Assert.Equal(119.09m, OfferPricing.Finalize(100m, Settings(), isOversized: false, chargeShipping: true));
        }

        [Fact]
        public void Outside_smart_the_shipping_cost_is_not_charged()
        {
            // Poza Smart kupujący płaci za przesyłkę osobno - wliczenie jej w cenę
            // obciążyłoby go dwa razy.
            Assert.Equal(110m, OfferPricing.Finalize(100m, Settings(), isOversized: false, chargeShipping: false));
        }

        [Fact]
        public void Finalize_adds_the_oversize_surcharge_last()
        {
            Assert.Equal(141.09m, OfferPricing.Finalize(100m, Settings(), isOversized: true, chargeShipping: true));
        }

        [Fact]
        public void Oversize_surcharge_applies_outside_smart_too()
        {
            // Dopłata za gabaryt to nasz koszt obsługi, nie koszt przesyłki Allegro.
            Assert.Equal(132m, OfferPricing.Finalize(100m, Settings(), isOversized: true, chargeShipping: false));
        }

        [Fact]
        public void Oversize_surcharge_is_skipped_when_not_configured()
        {
            var settings = Settings();
            settings.OversizeSurcharge = 0m;

            Assert.Equal(119.09m, OfferPricing.Finalize(100m, settings, isOversized: true, chargeShipping: true));
        }

        [Fact]
        public void Price_never_drops_below_one_zloty()
        {
            var settings = new PriceSettings();

            Assert.Equal(OfferPricing.MinimumPrice, OfferPricing.Finalize(0m, settings, isOversized: false, chargeShipping: true));
        }
    }
}
