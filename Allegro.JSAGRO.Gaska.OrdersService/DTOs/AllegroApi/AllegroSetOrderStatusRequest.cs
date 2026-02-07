using Allegro.JSAGRO.Gaska.OrdersService.Data.Enums;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Gaska.OrdersService.DTOs.AllegroApi
{
    public class AllegroSetOrderStatusRequest
    {
        [JsonPropertyName("status")]
        public AllegroOrderStatus Status { get; set; }
    }
}