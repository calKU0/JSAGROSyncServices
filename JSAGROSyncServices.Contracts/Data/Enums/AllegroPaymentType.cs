using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Contracts.Data.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AllegroPaymentType
    {
        CASH_ON_DELIVERY,
        WIRE_TRANSFER,
        ONLINE,
        SPLIT_PAYMENT,
        EXTENDED_TERM
    }
}