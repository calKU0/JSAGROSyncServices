using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Contracts.DTOs.Allegro
{
    public class AllegroShippingRateDetailsResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("features")]
        public Features Feat { get; set; }

        [JsonPropertyName("rates")]
        public List<Rate> Rates { get; set; }

        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        public class DeliveryMethod
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
        }

        public class Features
        {
            [JsonPropertyName("managedByAllegro")]
            public bool ManagedByAllegro { get; set; }

            [JsonPropertyName("isFulfillment")]
            public bool IsFulfillment { get; set; }
        }

        public class FirstItemRate
        {
            [JsonPropertyName("amount")]
            public string Amount { get; set; }

            [JsonPropertyName("currency")]
            public string Currency { get; set; }
        }

        public class MaxPackageWeight
        {
            [JsonPropertyName("value")]
            public string? Value { get; set; }

            [JsonPropertyName("unit")]
            public string? Unit { get; set; }
        }

        public class NextItemRate
        {
            [JsonPropertyName("amount")]
            public string? Amount { get; set; }

            [JsonPropertyName("currency")]
            public string? Currency { get; set; }
        }

        public class Rate
        {
            [JsonPropertyName("deliveryMethod")]
            public DeliveryMethod DeliveryMethod { get; set; }

            [JsonPropertyName("maxQuantityPerPackage")]
            public int? MaxQuantityPerPackage { get; set; }

            [JsonPropertyName("maxPackageWeight")]
            public MaxPackageWeight? MaxPackageWeight { get; set; }

            [JsonPropertyName("firstItemRate")]
            public FirstItemRate FirstItemRate { get; set; }

            [JsonPropertyName("nextItemRate")]
            public NextItemRate? NextItemRate { get; set; }

            [JsonPropertyName("shippingTime")]
            public ShippingTime? ShippingTime { get; set; }
        }

        public class ShippingTime
        {
            [JsonPropertyName("from")]
            public string? From { get; set; }

            [JsonPropertyName("to")]
            public string? To { get; set; }
        }
    }
}
