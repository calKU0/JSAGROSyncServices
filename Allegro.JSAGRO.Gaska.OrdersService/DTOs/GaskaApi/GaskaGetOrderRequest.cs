using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Gaska.OrdersService.DTOs.GaskaApi
{
    public class GaskaGetOrderRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("lng")]
        public int Lng { get; set; }
    }
}