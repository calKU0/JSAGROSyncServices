using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Models
{
    public class AllegroOrderItem
    {
        public int Id { get; set; }
        public int AllegroOrderId { get; set; }
        public int GaskaItemId { get; set; }
        public string OrderItemId { get; set; } = string.Empty;
        public string OfferId { get; set; } = string.Empty;
        public string OfferName { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string PriceGross { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? GaskaCourier { get; set; }
        public string? GaskaTrackingNumber { get; set; }
        public DateTime BoughtAt { get; set; }
    }
}