using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Data.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AllegroOrderStatus
    {
        NEW,
        PROCESSING,
        READY_FOR_SHIPMENT,
        READY_FOR_PICKUP,
        SENT,
        PICKED_UP,
        CANCELLED,
        SUSPENDED,
        RETURNED
    }
}