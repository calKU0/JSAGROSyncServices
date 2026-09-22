using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    /// <summary>
    /// Wpis słownikowy Erli - osoba odpowiedzialna i producent odpowiedzialny mają
    /// identyczny kształt, więc obsługuje je jeden typ i jedna ścieżka synchronizacji.
    /// </summary>
    public class ErliDictionaryEntry
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        /// <summary>Klucz z Allegro - po nim rozpoznajemy wpis już istniejący w Erli.</summary>
        [JsonPropertyName("idempotenceKey")]
        public string? IdempotenceKey { get; set; }

        [JsonPropertyName("properName")]
        public string? ProperName { get; set; }

        [JsonPropertyName("country")]
        public string? Country { get; set; }

        [JsonPropertyName("address")]
        public string? Address { get; set; }

        [JsonPropertyName("postalCode")]
        public string? PostalCode { get; set; }

        [JsonPropertyName("city")]
        public string? City { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("source")]
        public string Source { get; set; } = "allegro";
    }

    public class ErliDictionaryEntryResponse : ErliDictionaryEntry
    {
        // Erli zwraca id slownika jako liczbe, nie tekst - stad konwerter.
        [JsonPropertyName("id")]
        [JsonConverter(typeof(FlexibleStringConverter))]
        public string? Id { get; set; }
    }
}
