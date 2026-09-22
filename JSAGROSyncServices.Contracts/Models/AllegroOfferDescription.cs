namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroOfferDescription
    {
        public int Id { get; set; }
        public string OfferId { get; set; } = string.Empty;
        public virtual AllegroOffer? Offer { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public int SectionId { get; set; }
    }
}