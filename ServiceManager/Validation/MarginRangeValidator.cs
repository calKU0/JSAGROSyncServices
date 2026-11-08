using System.Text.Json.Nodes;

namespace ServiceManager.Validation
{
    /// <summary>Progi marży: od 0 PLN, bez wartości ujemnych, "Od" nie większe od "Do" i bez nakładania się.</summary>
    public static class MarginRangeValidator
    {
        public static IEnumerable<string> Validate(JsonArray rows)
        {
            var ranges = rows
                .Select(r => (Min: Read(r, "Min"), Max: Read(r, "Max")))
                .OrderBy(r => r.Min)
                .ToList();

            var errors = new List<string>();

            if (ranges.Count == 0)
            {
                errors.Add(ValidationMessages.MarginMissingRange);
                return errors;
            }

            if (ranges.Any(r => r.Min < 0 || r.Max < 0))
                errors.Add(ValidationMessages.MarginNegative);

            if (ranges.Any(r => r.Min > r.Max))
                errors.Add(ValidationMessages.MarginMinGreaterThanMax);

            if (ranges[0].Min != 0m)
                errors.Add(ValidationMessages.MarginMustStartAtZero);

            for (var i = 0; i < ranges.Count - 1; i++)
            {
                if (ranges[i + 1].Min <= ranges[i].Max)
                    errors.Add(ValidationMessages.MarginOverlap);
            }

            return errors.Distinct();
        }

        private static decimal Read(JsonNode? row, string property) =>
            row?[property] is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : 0m;
    }
}
