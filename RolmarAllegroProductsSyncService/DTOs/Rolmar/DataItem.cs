using System.Text.Json.Serialization;

namespace RolmarAllegroProductsSyncService.DTOs.Rolmar
{
    public class DataItem
    {
        [JsonPropertyName("param")]
        public List<ParamItem> Param { get; set; } = new List<ParamItem>();
    }

    public class ParamItem
    {
        [JsonPropertyName("productIndex")]
        public string? ProductIndex { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("categorySeparator")]
        public string? CategorySeparator { get; set; }
    }
}