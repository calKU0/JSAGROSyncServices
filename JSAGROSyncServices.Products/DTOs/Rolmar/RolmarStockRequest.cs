using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.DTOs.Rolmar
{
    public class RolmarStockRequest
    {
        [JsonPropertyName("data")]
        public List<DataItem> Data { get; set; } = new List<DataItem>();
    }
}