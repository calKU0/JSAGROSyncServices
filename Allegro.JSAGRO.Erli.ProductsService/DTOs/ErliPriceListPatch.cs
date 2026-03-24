using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    public class ErliPriceListPatch
    {
        [JsonPropertyName("prices")]
        public List<Prices> Prices { get; set; }

        [JsonPropertyName("erliProEnabled")]
        public bool ErliProEnabled { get; set; }

        [JsonPropertyName("nextDayDeliveryEnabled")]
        public bool NextDayDeliveryEnabled { get; set; }
    }
}
