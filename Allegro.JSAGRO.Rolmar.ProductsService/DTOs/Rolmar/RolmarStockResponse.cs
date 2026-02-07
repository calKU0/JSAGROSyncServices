using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Rolmar.ProductsService.DTOs.Rolmar
{
    public class RolmarStockResponse
    {
        [JsonPropertyName("result")]
        public List<StockItem> StockItems { get; set; }
    }

    public class StockItem
    {
        [JsonPropertyName("stock")]
        public int Stock { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("index")]
        public string Index { get; set; }
    }
}