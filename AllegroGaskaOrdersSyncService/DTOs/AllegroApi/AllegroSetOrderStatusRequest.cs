using AllegroGaskaOrdersSyncService.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.AllegroApi
{
    public class AllegroSetOrderStatusRequest
    {
        [JsonPropertyName("status")]
        public AllegroOrderStatus Status { get; set; }
    }
}