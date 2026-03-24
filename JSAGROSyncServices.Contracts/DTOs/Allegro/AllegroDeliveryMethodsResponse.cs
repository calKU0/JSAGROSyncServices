using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Contracts.DTOs.Allegro
{
    public class AllegroDeliveryMethodsResponse
    {
        [JsonPropertyName("deliveryMethods")]
        public List<DeliveryMethod> DeliveryMethods { get; set; }
        public class Default
        {
            [JsonPropertyName("from")]
            public string From { get; set; }

            [JsonPropertyName("to")]
            public string To { get; set; }
        }

        public class DeliveryMethod
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("marketplaces")]
            public List<string> Marketplaces { get; set; }

            [JsonPropertyName("paymentPolicy")]
            public string PaymentPolicy { get; set; }

            [JsonPropertyName("allegroEndorsed")]
            public bool AllegroEndorsed { get; set; }

            [JsonPropertyName("dispatchCountry")]
            public object DispatchCountry { get; set; }

            [JsonPropertyName("destinationCountry")]
            public string DestinationCountry { get; set; }

            [JsonPropertyName("shippingRatesConstraints")]
            public ShippingRatesConstraints ShippingRatesConstraints { get; set; }
        }

        public class FirstItemRate
        {
            [JsonPropertyName("min")]
            public string Min { get; set; }

            [JsonPropertyName("max")]
            public string Max { get; set; }

            [JsonPropertyName("currency")]
            public string Currency { get; set; }
        }

        public class MaxPackageWeight
        {
            [JsonPropertyName("supported")]
            public bool Supported { get; set; }

            [JsonPropertyName("min")]
            public string Min { get; set; }

            [JsonPropertyName("max")]
            public string Max { get; set; }

            [JsonPropertyName("unit")]
            public string Unit { get; set; }
        }

        public class MaxQuantityPerPackage
        {
            [JsonPropertyName("max")]
            public int Max { get; set; }
        }

        public class NextItemRate
        {
            [JsonPropertyName("min")]
            public string Min { get; set; }

            [JsonPropertyName("max")]
            public string Max { get; set; }

            [JsonPropertyName("currency")]
            public string Currency { get; set; }
        }

        public class ShippingRatesConstraints
        {
            [JsonPropertyName("allowed")]
            public bool Allowed { get; set; }

            [JsonPropertyName("maxQuantityPerPackage")]
            public MaxQuantityPerPackage MaxQuantityPerPackage { get; set; }

            [JsonPropertyName("maxPackageWeight")]
            public MaxPackageWeight MaxPackageWeight { get; set; }

            [JsonPropertyName("firstItemRate")]
            public FirstItemRate FirstItemRate { get; set; }

            [JsonPropertyName("nextItemRate")]
            public NextItemRate NextItemRate { get; set; }

            [JsonPropertyName("shippingTime")]
            public ShippingTime ShippingTime { get; set; }
        }

        public class ShippingTime
        {
            [JsonPropertyName("default")]
            public Default Default { get; set; }

            [JsonPropertyName("customizable")]
            public bool Customizable { get; set; }
        }
    }
}
