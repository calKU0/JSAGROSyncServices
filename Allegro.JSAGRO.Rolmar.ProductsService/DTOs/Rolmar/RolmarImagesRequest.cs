using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Rolmar.ProductsService.DTOs.Rolmar
{
    public class RolmarImagesRequest
    {
        [JsonPropertyName("data")]
        public List<DataItem> Data { get; set; } = new List<DataItem>();
    }
}