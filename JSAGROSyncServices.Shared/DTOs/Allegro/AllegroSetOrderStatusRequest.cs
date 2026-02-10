using JSAGROSyncServices.Shared.Data.Enums;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Shared.DTOs.Allegro
{
    public class AllegroSetOrderStatusRequest
    {
        [JsonPropertyName("status")]
        public AllegroOrderStatus Status { get; set; }
    }
}