namespace JSAGROSyncServices.Orders.Configuration
{
    /// <summary>
    /// Reguły realizacji zamówień, które różnią się między kontami.
    /// Reszta przebiegu jest wspólna.
    /// </summary>
    public sealed class OrderPipeline
    {
        /// <summary>
        /// Cenniki dostawy, dla których towar jedzie prosto od dostawcy do klienta (dropshipping).
        /// Pusta lista oznacza, że wszystkie zamówienia są wysyłane do klienta.
        /// Pozycje spoza tej listy jadą na domyślny adres dostawy w Gąsce (własny magazyn).
        /// </summary>
        public IReadOnlyList<string> DropshippingDeliveryNames { get; init; } = Array.Empty<string>();

        /// <summary>Kurier używany przy wysyłce do własnego magazynu.</summary>
        public string WarehouseCourier { get; init; } = "GLS";

        /// <summary>Czy wszystkie zamówienia idą bezpośrednio do klienta.</summary>
        public bool IsAlwaysDropshipping => DropshippingDeliveryNames.Count == 0;

        /// <summary>
        /// Czy zamówienie jedzie prosto do klienta. Decyduje cennik dostawy pozycji zamówienia -
        /// wystarczy jedna pozycja z cennika dropshippingowego.
        /// </summary>
        public bool IsDropshipping(IEnumerable<string?>? itemShippingRates)
        {
            if (IsAlwaysDropshipping)
                return true;

            if (itemShippingRates == null)
                return false;

            return itemShippingRates.Any(rate => !string.IsNullOrWhiteSpace(rate)
                && DropshippingDeliveryNames.Any(d => rate!.Contains(d, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
