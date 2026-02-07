using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Rolmar.ProductsService.DTOs.Rolmar
{
    public class RolmarImagesResponse
    {
        [JsonPropertyName("result")]
        public List<PhotoItem> PhotoItems { get; set; }
    }

    public class PhotoItem
    {
        [JsonPropertyName("main")]
        public string Main { get; set; }

        [JsonPropertyName("index")]
        public string Index { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}