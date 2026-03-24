using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Contracts.DTOs.Allegro
{
    public class AllegroResponsiblePersonsResult
    {
        [JsonPropertyName("responsiblePersons")]
        public List<ResponsiblePerson> ResponsiblePersons { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("totalCount")]
        public int TotalCount { get; set; }

        public class Address
        {
            [JsonPropertyName("countryCode")]
            public string CountryCode { get; set; }

            [JsonPropertyName("street")]
            public string Street { get; set; }

            [JsonPropertyName("postalCode")]
            public string PostalCode { get; set; }

            [JsonPropertyName("city")]
            public string City { get; set; }
        }

        public class Contact
        {
            [JsonPropertyName("email")]
            public string Email { get; set; }

            [JsonPropertyName("phoneNumber")]
            public string PhoneNumber { get; set; }

            [JsonPropertyName("formUrl")]
            public string FormUrl { get; set; }
        }

        public class PersonalData
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("address")]
            public Address Address { get; set; }

            [JsonPropertyName("contact")]
            public Contact Contact { get; set; }
        }

        public class ResponsiblePerson
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("personalData")]
            public PersonalData PersonalData { get; set; }
        }
    }
}
