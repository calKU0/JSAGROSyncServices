using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroOffer
    {
        public string Id { get; set; } = string.Empty;
        public AllegroAccount Account { get; set; }
        public string? ProductId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int WatchersCount { get; set; }
        public int VisitsCount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DeliveryName { get; set; } = string.Empty;
        public DateTime StartingAt { get; set; }
        public bool ExistsInErli { get; set; } = false;
        public string Images { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public string HandlingTime { get; set; } = string.Empty;
        public string ResponsibleProducer { get; set; } = string.Empty;
        public string ResponsiblePerson { get; set; } = string.Empty;
        public virtual RolmarProduct? Product { get; set; }
        public virtual List<AllegroOfferDescription> Descriptions { get; set; } = new();
        public virtual List<AllegroOfferAttribute> Attributes { get; set; } = new();
    }
}