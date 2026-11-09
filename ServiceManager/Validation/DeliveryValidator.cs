using System.Text.Json.Nodes;

namespace ServiceManager.Validation
{
    /// <summary>Cenniki dostaw Allegro: wymiary i waga muszą być większe od zera.</summary>
    public static class DeliveryValidator
    {
        public static IEnumerable<string> Validate(JsonArray rows)
        {
            var dimensions = new[] { "Length", "Width", "Height", "Weight" };

            if (rows.Any(r => dimensions.Any(d => Read(r, d) <= 0)))
                yield return ValidationMessages.DeliveryNonPositive;
        }

        private static decimal Read(JsonNode? row, string property) =>
            row?[property] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0m;
    }
}
