using System.Text.Json;

namespace JSAGROSyncServices.Infrastructure.Helpers
{
    /// <summary>
    /// Dopasowanie kategorii dostawcy do listy skonfigurowanej dla konta.
    /// Brak skonfigurowanych kategorii (null) oznacza brak filtrowania.
    /// </summary>
    public static class CategoryFilter
    {
        public const char CategorySeparator = '>';

        public static string? ToJson(IEnumerable<string>? categories)
        {
            var values = categories?
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            return values.Count == 0 ? null : JsonSerializer.Serialize(values);
        }

        public static string? ToJson(IEnumerable<int>? categoryIds)
        {
            var values = categoryIds?
                .Where(id => id != 0)
                .Distinct()
                .Select(id => id.ToString())
                .ToList() ?? new List<string>();

            return values.Count == 0 ? null : JsonSerializer.Serialize(values);
        }

        /// <summary>
        /// Czy ścieżka kategorii dostawcy należy do którejś ze skonfigurowanych kategorii.
        /// Skonfigurowana kategoria obejmuje wszystkie swoje podkategorie:
        /// "WARYŃSKI ORIGIN" pasuje do "WARYŃSKI ORIGIN>Narzędzia", ale już nie do "WARYŃSKI ORIGINAL>...".
        /// </summary>
        public static bool Matches(string? categoryPath, IEnumerable<string>? configuredCategories)
        {
            if (configuredCategories == null)
                return false;

            var path = Normalize(categoryPath);

            if (path.Length == 0)
                return false;

            foreach (var configured in configuredCategories)
            {
                var prefix = Normalize(configured);

                if (prefix.Length == 0 || path.Length < prefix.Length)
                    continue;

                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Dopasowanie musi kończyć się na granicy segmentu ścieżki.
                if (path.Length == prefix.Length || path[prefix.Length] == CategorySeparator)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Ujednolica zapis ścieżki - dostawca i konfiguracja różnie traktują spacje wokół separatora.
        /// </summary>
        public static string Normalize(string? category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return string.Empty;

            return category
                .Trim()
                .Replace(" >", ">", StringComparison.Ordinal)
                .Replace("> ", ">", StringComparison.Ordinal);
        }
    }
}
