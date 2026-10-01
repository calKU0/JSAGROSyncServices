using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Wartości parametrów, których dane dostawców nie zawierają, a Allegro wymaga ich
    /// do wystawienia oferty. Każda jest albo faktem niezależnym od produktu (sprzedajemy
    /// jedną pozycję), albo wartością ze słownika kategorii, która niczego nie zawęża -
    /// zgadywanie konkretu, na przykład strony zabudowy, wprowadzałoby kupujących w błąd.
    /// </summary>
    public static class ParameterDefaults
    {
        /// <summary>
        /// Parametry w rodzinie "Liczba ... w ofercie" (tarcz, klocków, sztuk). Allegro wymaga
        /// ich w wielu kategoriach części, a dostawcy nie podają liczby sztuk w opakowaniu.
        /// </summary>
        private static readonly Regex CountInOfferName = new(
            @"^liczba\b.*\bw\s+ofercie$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Wartości "Strony zabudowy" oznaczające brak zawężenia, od najbardziej do najmniej
        /// wprost. Kolejność jest istotna: bierzemy pierwszą, którą kategoria dopuszcza.
        /// </summary>
        private static readonly string[] UniversalMountingSides =
        [
            "uniwersalne",
            "uniwersalna",
            "uniwersalny",
            "przód + tył",
            "lewe + prawe",
            "dowolna",
            "bez znaczenia"
        ];

        /// <summary>Wartość dla "Liczba ... w ofercie".</summary>
        public const string CountInOfferValue = "1";

        /// <summary>
        /// Ten sam warunek co <see cref="IsCountInOffer"/>, w składni LIKE - procedura
        /// uzupełniająca parametry w bazie musi dobierać dokładnie te same parametry.
        /// </summary>
        public const string CountInOfferSqlPattern = "liczba%w ofercie";

        /// <summary>Nazwa parametru "Strona zabudowy", tak jak nazywa go Allegro.</summary>
        public const string MountingSideParameterName = "Strona zabudowy";

        /// <summary>Lista preferencji dla "Strony zabudowy" - kolejność jest istotna.</summary>
        public static IReadOnlyList<string> UniversalMountingSidesOrdered => UniversalMountingSides;

        /// <summary>Czy parametr pyta o liczbę sztuk w ofercie. Zawsze odpowiadamy 1.</summary>
        public static bool IsCountInOffer(string? parameterName) =>
            !string.IsNullOrWhiteSpace(parameterName) && CountInOfferName.IsMatch(parameterName.Trim());

        /// <summary>
        /// Wartość "Strony zabudowy" ze słownika kategorii, która nie zawęża zastosowania.
        /// <c>null</c>, gdy kategoria żadnej takiej nie dopuszcza - wtedy parametr zostaje pusty,
        /// bo wartości spoza słownika Allegro odrzuca razem z całą ofertą.
        /// </summary>
        public static string? UniversalMountingSide(IEnumerable<string?>? allowedValues) =>
            FirstAllowed(allowedValues, UniversalMountingSides);

        /// <summary>Pierwsza z <paramref name="preferred"/> wartości, którą słownik faktycznie dopuszcza.</summary>
        public static string? FirstAllowed(IEnumerable<string?>? allowedValues, IEnumerable<string> preferred)
        {
            if (allowedValues == null)
                return null;

            var allowed = allowedValues
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .ToList();

            if (allowed.Count == 0)
                return null;

            foreach (var candidate in preferred)
            {
                var match = allowed.FirstOrDefault(value => value.Equals(candidate, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    return match;
            }

            return null;
        }
    }
}
