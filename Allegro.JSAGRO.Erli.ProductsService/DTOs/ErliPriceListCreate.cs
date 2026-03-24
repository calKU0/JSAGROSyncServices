using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    public class ErliPriceListCreate
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("prices")]
        public List<Prices> Prices { get; set; }

        [JsonPropertyName("erliProEnabled")]
        public bool ErliProEnabled { get; set; }

        [JsonPropertyName("nextDayDeliveryEnabled")]
        public bool NextDayDeliveryEnabled { get; set; }
    }
}
