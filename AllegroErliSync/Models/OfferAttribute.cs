namespace AllegroErliSync.Models
{
    public class OfferAttribute
    {
        public int Id { get; set; }
        public string OfferId { get; set; }
        public string AttributeId { get; set; }
        public string Type { get; set; }
        public string ValuesJson { get; set; }
        public string ValuesIdsJson { get; set; }
    }
}