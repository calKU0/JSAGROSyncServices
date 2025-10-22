using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.GaskaApi
{
    public class GaskaCreateOrderItemRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("qty")]
        public string Qty { get; set; } = string.Empty;

        [JsonPropertyName("Notice")]
        public string? Notice { get; set; }
    }
}