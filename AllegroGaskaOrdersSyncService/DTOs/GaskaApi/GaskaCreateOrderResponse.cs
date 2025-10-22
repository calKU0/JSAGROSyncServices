using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.GaskaApi
{
    public class GaskaCreateOrderResponse
    {
        [JsonPropertyName("newOrders")]
        public List<int> NewOrders { get; set; } = new();

        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}