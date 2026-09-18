namespace JSAGROSyncServices.Infrastructure.Helpers
{
    public static class PriceHelper
    {
        /// <summary>
        /// Czy cennik dostawy oferty jest zarządzany ręcznie na Allegro.
        /// Dla takich ofert serwis nie wysyła ceny, cennika dostawy ani czasu realizacji -
        /// te wartości ustawia sprzedawca i nadpisanie ich cofnęłoby jego zmiany.
        /// </summary>
        public static bool IsManuallyManagedDelivery(string? offerDeliveryName, List<string>? manuallyManagedDeliveries)
        {
            if (string.IsNullOrWhiteSpace(offerDeliveryName) || manuallyManagedDeliveries == null)
                return false;

            return manuallyManagedDeliveries.Contains(offerDeliveryName, StringComparer.OrdinalIgnoreCase);
        }
    }
}
