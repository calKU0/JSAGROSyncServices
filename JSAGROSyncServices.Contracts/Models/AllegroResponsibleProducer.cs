using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroResponsibleProducer
    {

        public int Id { get; set; }
        public string AllegroId { get; set; }
        public AllegroAccount Account { get; set; }
        public string Name { get; set; }
        public string TradeName { get; set; }
        public string CountryCode { get; set; }
        public string Street { get; set; }
        public string PostalCode { get; set; }
        public string City { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? FormUrl { get; set; }

    }
}
