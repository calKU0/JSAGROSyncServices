using System.Text.Json.Serialization;

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