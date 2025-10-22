using AllegroGaskaOrdersSyncService.Data.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Models
{
    public class AllegroOrder
    {
        public int Id { get; set; }
        public string AllegroId { get; set; } = string.Empty;
        public string MessageToSeller { get; set; } = string.Empty;
        public string? Note { get; set; }
        public AllegroCheckoutFormStatus Status { get; set; }
        public AllegroOrderStatus RealizeStatus { get; set; }
        public decimal Amount { get; set; }
        public string RecipientFirstName { get; set; } = string.Empty;
        public string RecipientLastName { get; set; } = string.Empty;
        public string RecipientStreet { get; set; } = string.Empty;
        public string RecipientCity { get; set; } = string.Empty;
        public string RecipientPostalCode { get; set; } = string.Empty;
        public string RecipientCountry { get; set; } = string.Empty;
        public string? RecipientCompanyName { get; set; }
        public string? RecipientEmail { get; set; } = string.Empty;
        public string? RecipientPhoneNumber { get; set; }
        public string DeliveryMethodId { get; set; } = string.Empty;
        public string DeliveryMethodName { get; set; } = string.Empty;
        public DateTime? CancellationDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Revision { get; set; } = string.Empty;
        public AllegroPaymentType PaymentType { get; set; }
        public bool SentToGaska { get; set; }
        public int GaskaOrderId { get; set; }
        public string? GaskaOrderStatus { get; set; }
        public string? GaskaOrderNumber { get; set; }
        public string? GaskaDeliveryName { get; set; }
        public List<AllegroOrderItem> Items { get; set; } = new();
    }
}