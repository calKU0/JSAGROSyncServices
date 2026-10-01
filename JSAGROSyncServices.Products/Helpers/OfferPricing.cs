using JSAGROSyncServices.Products.Settings;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Narzuty doliczane do ceny oferty, wspólne dla wszystkich dostawców. Wcześniej te same
    /// drabinki były przepisane w każdej fabryce ofert z osobna i zdążyły się rozjechać.
    ///
    /// Kolejność jest istotna: najpierw marża własna (liczona przez dostawcę, bo zależy od jego
    /// cennika zakupu), potem prowizja Allegro, a na końcu koszt wysyłki - prowizję Allegro
    /// liczy się od ceny towaru, a nie od ceny z doliczoną wysyłką.
    /// </summary>
    public static class OfferPricing
    {
        /// <summary>Najniższa cena oferty, jaką wolno wystawić.</summary>
        public const decimal MinimumPrice = 1.00m;

        /// <summary>
        /// Próg ceny oferty i odpowiadający mu koszt wysyłki w abonamencie Allegro Smart.
        /// Progi są rosnące - bierzemy ostatni, który cena przekracza.
        /// </summary>
        private static readonly (decimal From, decimal Cost)[] ShippingCosts =
        [
            (30m, 1.99m),
            (45m, 3.99m),
            (65m, 5.79m),
            (100m, 9.09m),
            (150m, 11.49m)
        ];

        /// <summary>
        /// Prowizja Allegro doliczona do ceny towaru. Trzy progi: kwota stała poniżej 5 zł,
        /// procent w przedziale 5-1000 zł i kwota stała powyżej 1000 zł. Przejście progu
        /// w wyniku samego naliczenia przełącza na regułę wyższego progu.
        /// </summary>
        public static decimal AddAllegroCommission(decimal price, PriceSettings priceSettings)
        {
            if (price < 5m)
            {
                var withFlatFee = price + priceSettings.AllegroMarginUnder5PLN;

                return withFlatFee < 5m
                    ? withFlatFee
                    : price * (1 + priceSettings.AllegroMarginBetween5and1000PLNPercent / 100m);
            }

            if (price <= 1000m)
            {
                var withPercent = price * (1 + priceSettings.AllegroMarginBetween5and1000PLNPercent / 100m);

                return withPercent > 1000m
                    ? price + priceSettings.AllegroMarginMoreThan1000PLN
                    : withPercent;
            }

            return price + priceSettings.AllegroMarginMoreThan1000PLN;
        }

        /// <summary>
        /// Koszt wysyłki doliczany do ceny oferty. Poniżej najniższego progu Allegro pobiera
        /// część opłaty proporcjonalnie do ceny.
        /// </summary>
        public static decimal ShippingCost(decimal price)
        {
            var (firstThreshold, firstCost) = ShippingCosts[0];

            if (price < firstThreshold)
                return Math.Min(firstCost, Math.Round(price / firstThreshold * firstCost, 2));

            var cost = firstCost;

            foreach (var (from, value) in ShippingCosts)
            {
                if (price >= from)
                    cost = value;
            }

            return cost;
        }

        /// <summary>
        /// Prowizja Allegro, koszt wysyłki i dopłata za gabaryt - końcówka wyliczenia ceny,
        /// identyczna u każdego dostawcy. <paramref name="price"/> to cena towaru z marżą własną.
        /// </summary>
        /// <param name="price">Cena towaru z doliczoną marżą własną dostawcy.</param>
        /// <param name="priceSettings">Progi prowizji i dopłat z konfiguracji serwisu.</param>
        /// <param name="isOversized">Czy produkt przekracza próg gabarytu.</param>
        /// <param name="chargeShipping">
        /// Czy doliczyć koszt wysyłki. Tylko dla cenników objętych Allegro Smart - poza Smart
        /// za przesyłkę płaci kupujący osobno, więc wliczenie jej w cenę podniosłoby ją dwukrotnie.
        /// </param>
        public static decimal Finalize(decimal price, PriceSettings priceSettings, bool isOversized, bool chargeShipping)
        {
            var finalPrice = AddAllegroCommission(price, priceSettings);

            if (chargeShipping)
                finalPrice += ShippingCost(finalPrice);

            if (isOversized && priceSettings.OversizeSurcharge > 0)
                finalPrice += priceSettings.OversizeSurcharge;

            return Math.Max(finalPrice, MinimumPrice);
        }
    }
}
