using JSAGROSyncServices.Contracts.Data.Enums;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Contracts.DTOs.Allegro
{
    public class AllegroSetOrderStatusRequest
    {
        [JsonPropertyName("status")]
        public AllegroOrderStatus Status { get; set; }
    }
}