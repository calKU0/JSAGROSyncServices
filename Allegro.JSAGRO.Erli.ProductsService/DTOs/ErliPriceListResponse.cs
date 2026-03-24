using Allegro.JSAGRO.Erli.ProductsService.Enums;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    public class ErliPriceListResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("prices")]
        public List<Prices> Prices { get; set; }

        [JsonPropertyName("erliProEnabled")]
        public bool ErliProEnabled { get; set; }

        [JsonPropertyName("nextDayDeliveryEnabled")]
        public bool NextDayDeliveryEnabled { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class Prices
    {
        [JsonPropertyName("deliveryMethod")]
        public DeliveryMethod DeliveryMethod { get; set; }

        [JsonPropertyName("basePrice")]
        public int BasePrice { get; set; }
        [JsonPropertyName("nextItemPrice")]
        public int NextItemPrice { get; set; }

        [JsonPropertyName("limit")]
        public object Limit { get; set; }

        [JsonPropertyName("nextDayDeliveryOption")]
        public bool? NextDayDeliveryOption { get; set; }
    }

    public class DeliveryDimensionLimit
    {
        [JsonPropertyName("dimension")]
        public string Dimension { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }
    }

    public class DeliveryMethod
    {
        [JsonPropertyName("id")]
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public ErliDeliveryMethod Id { get; set; }

        [JsonPropertyName("deliveryTime")]
        public DeliveryTime? DeliveryTime { get; set; }
    }

    public class DeliveryTime
    {
        [JsonPropertyName("unit")]
        public string Unit { get; set; }
        [JsonPropertyName("minPeriod")]
        public int MinPeriod { get; set; }
        [JsonPropertyName("MaxPeriod")]
        public int MaxPeriod { get; set; }
    }
}
