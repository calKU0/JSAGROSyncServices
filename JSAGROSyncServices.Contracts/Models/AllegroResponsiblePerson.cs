using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class AllegroResponsiblePerson
    {
        public int Id { get; set; }
        public string AllegroId { get; set; } = string.Empty;
        public AllegroAccount Account { get; set; }
        public string Name { get; set; } = string.Empty;
        public string PersonName { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public string Street { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? FormUrl { get; set; }
    }
}
