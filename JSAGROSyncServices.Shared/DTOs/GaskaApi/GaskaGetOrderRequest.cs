using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Shared.DTOs.Allegro.GaskaApi
{
    public class GaskaGetOrderRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("lng")]
        public int Lng { get; set; }
    }
}