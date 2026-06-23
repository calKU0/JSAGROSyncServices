namespace JSAGROSyncServices.Infrastructure.Helpers
{
    public static class PriceHelper
    {
        public static bool ShouldUpdatePriceAndDelivery(string offerDelivery, List<string> deliveryList)
        {
            if (deliveryList.Contains(offerDelivery, StringComparer.OrdinalIgnoreCase))
                return false;

            return true;
        }
    }
}
