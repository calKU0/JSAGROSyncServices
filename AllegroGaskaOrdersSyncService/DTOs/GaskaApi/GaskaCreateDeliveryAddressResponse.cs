using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.GaskaApi
{
    public class GaskaCreateDeliveryAddressResponse
    {
        [JsonPropertyName("addressID")]
        public int AddressId { get; set; }

        [JsonPropertyName("result")]
        public int Result { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }
}