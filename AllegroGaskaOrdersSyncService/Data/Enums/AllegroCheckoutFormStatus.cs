using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Data.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AllegroCheckoutFormStatus
    {
        BOUGHT,
        FILLED_IN,
        READY_FOR_PROCESSING,
        CANCELLED
    }
}