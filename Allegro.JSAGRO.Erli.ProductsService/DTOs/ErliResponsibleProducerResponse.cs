using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    public class ErliResponsibleProducerResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("idempotenceKey")]
        public string IdempotenceKey { get; set; }

        [JsonPropertyName("properName")]
        public string ProperName { get; set; }

        [JsonPropertyName("country")]
        public string Country { get; set; }

        [JsonPropertyName("address")]
        public string Address { get; set; }

        [JsonPropertyName("postalCode")]
        public string PostalCode { get; set; }

        [JsonPropertyName("city")]
        public string City { get; set; }

        [JsonPropertyName("phone")]
        public string Phone { get; set; }

        [JsonPropertyName("email")]
        public string Email { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; }
    }
}
