using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Gaska.OrdersService.Data.Enums
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