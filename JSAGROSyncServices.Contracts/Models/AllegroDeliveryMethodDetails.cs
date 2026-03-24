using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroDeliveryMethodDetails
    {
        public int Id { get; set; }
        public int AllegroDeliveryMethodId { get; set; }
        public string Name { get; set; }
        public PaymentPolicy PaymentPolicy { get; set; }
        public int? MaxPackageQuantity { get; set; }
        public decimal? MaxPackageWeight { get; set; }
        public string? MaxPackageWeightUnit { get; set; }
        public decimal FirstItemAmount { get; set; }
        public string FirstItemCurrency { get; set; }
        public decimal? NextItemAmount { get; set; }
        public string? NextItemCurrency { get; set; }
        public string? ShippingTimeFrom { get; set; }
        public string? ShippingTimeTo { get; set; }
    }
}
