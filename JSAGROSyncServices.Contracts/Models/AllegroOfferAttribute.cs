namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroOfferAttribute
    {
        public int Id { get; set; }
        public string OfferId { get; set; } = string.Empty;
        public virtual AllegroOffer? Offer { get; set; }
        public string AttributeId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string ValuesJson { get; set; } = string.Empty;
        public string ValuesIdsJson { get; set; } = string.Empty;
    }
}