using System.Text.Json.Serialization;

namespace RolmarAllegroProductsSyncService.DTOs.Rolmar
{
    public class RolmarStockRequest
    {
        [JsonPropertyName("data")]
        public List<DataItem> Data { get; set; } = new List<DataItem>();
    }
}