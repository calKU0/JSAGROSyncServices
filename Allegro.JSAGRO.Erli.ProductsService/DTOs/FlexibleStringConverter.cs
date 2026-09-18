using System.Text.Json;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.DTOs
{
    /// <summary>
    /// Czyta wartość tekstową także wtedy, gdy Erli zwróci ją jako liczbę.
    /// Identyfikatory słownikowe przychodzą jako liczby (`"id":15187`), a w innych
    /// miejscach jako tekst - bez tego konwertera odczyt odpowiedzi wysypuje się
    /// na "The JSON value could not be converted to System.String".
    /// </summary>
    public class FlexibleStringConverter : JsonConverter<string?>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.TryGetInt64(out var number)
                    ? number.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Nie można odczytać wartości tekstowej z tokenu {reader.TokenType}.")
            };

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (value == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(value);
        }
    }
}
