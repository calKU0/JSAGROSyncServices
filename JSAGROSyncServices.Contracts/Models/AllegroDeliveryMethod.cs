using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroDeliveryMethod
    {
        public int Id { get; set; }
        public string AllegroId { get; set; }
        public AllegroAccount Account { get; set; }
        public string Name { get; set; }
        public bool ManagedByAllegro { get; set; }
        public bool IsFulfillment { get; set; }
        public List<AllegroDeliveryMethodDetails> AllegroDeliveryMethodDetails { get; set; }
    }
}
