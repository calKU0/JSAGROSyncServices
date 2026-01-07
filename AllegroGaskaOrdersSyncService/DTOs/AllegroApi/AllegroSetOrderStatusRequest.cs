using AllegroGaskaOrdersSyncService.Data.Enums;
using System.Text.Json.Serialization;

namespace AllegroGaskaOrdersSyncService.DTOs.AllegroApi
{
    public class AllegroSetOrderStatusRequest
    {
        [JsonPropertyName("status")]
        public AllegroOrderStatus Status { get; set; }
    }
}