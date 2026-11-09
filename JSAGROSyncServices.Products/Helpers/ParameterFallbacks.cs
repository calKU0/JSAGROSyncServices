using JSAGROSyncServices.Contracts.Models;
using System.Globalization;
using System.Text;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Wartości zastępcze parametrów, których nie da się wyliczyć z danych dostawcy, oraz wybór
    /// zestawu parametrów pasującego do kategorii oferty.
    /// </summary>
    public static class ParameterFallbacks
    {
        /// <summary>
        /// Wartości słownikowe oznaczające "nie wiadomo" / "inne" - używane tylko dla parametrów
        /// obowiązkowych, gdy kategoria nie ma wskazanej przez Allegro wartości niejednoznacznej.
        /// Kolejność = pierwszeństwo.
        /// </summary>
        /// <summary>Wartość słownikowa "Marki" dla towaru bez marki.</summary>
        public const string UnbrandedValue = "bez marki";

        public static readonly IReadOnlyList<string> GenericValues =
        [
            "nieznany producent", "nieznana marka", "inna marka", "inna", "inny", "inne",
            "brak informacji", "niezdefiniowany", "nie dotyczy", "pozostałe", "pozostałe marki", "bez marki"
        ];

        /// <summary>
        /// Wartość ze słownika kategorii odpowiadająca podanej (bez względu na wielkość liter, polskie
        /// znaki i spacje). <c>null</c>, gdy słownik jej nie zawiera. Parametr bez słownika (tekst, liczba)
        /// zwraca wartość bez zmian.
        /// </summary>
        public static string? FitToDictionary(CategoryParameter param, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (!IsDictionary(param))
                return value;

            var key = Key(value);
            return param.Values.FirstOrDefault(v => Key(v.Value) == key)?.Value;
        }

        /// <summary>
        /// Wartość dla parametru obowiązkowego, którego nie umiemy wyliczyć. Najpierw wartość
        /// niejednoznaczna wskazana przez Allegro ("Inna", "nieznany producent"), potem ogólna
        /// wartość ze słownika. Parametru bez słownika nie zmyślamy - poza liczbą sztuk.
        /// </summary>
        public static string? ForRequired(CategoryParameter param, RolmarProduct product)
        {
            var name = param.Name?.Trim().ToLowerInvariant() ?? string.Empty;

            if (IsPackageCount(name))
                return FitToDictionary(param, ParameterDefaults.CountInOfferValue);

            if (!IsDictionary(param))
            {
                // Tekstowe pola identyfikujące część - mamy je zawsze.
                if (name.Contains("numer katalogowy"))
                    return product.Code;

                if (name.StartsWith("producent"))
                    return string.IsNullOrWhiteSpace(product.SupplierName) ? "nieznany producent" : product.SupplierName.Trim();

                if (name == "marka")
                    return string.IsNullOrWhiteSpace(product.SupplierName) ? "bez marki" : product.SupplierName.Trim();

                if (name.StartsWith("marka"))
                    return "Inna";

                return null;
            }

            // "Marka" to marka samego towaru. Allegro odrzuca dla niej wartość niejednoznaczną
            // ("An incorrect value `Inna` has been entered for parameter `Marka`") - towar bez
            // znanej marki to w słowniku "bez marki".
            if (name == "marka")
            {
                var unbranded = FitToDictionary(param, UnbrandedValue);
                if (unbranded != null)
                    return unbranded;
            }

            // Etykieta wartości niejednoznacznej tylko wtedy, gdy słownik kategorii ją zawiera -
            // spoza słownika Allegro odrzuca ofertę błędem DictionaryParameterValueNotFound.
            if (!string.IsNullOrWhiteSpace(param.AmbiguousValue))
            {
                var ambiguous = FitToDictionary(param, param.AmbiguousValue);
                if (ambiguous != null)
                    return ambiguous;
            }

            foreach (var generic in GenericValues)
            {
                var match = FitToDictionary(param, generic);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static readonly char[] NumberSeparators = [',', ' '];
        private static readonly char[] NameSeparators = [',', ';'];

        /// <summary>
        /// Separatory wartości parametru wielowartościowego. Numery katalogowe nie zawierają spacji,
        /// więc dzielimy je także po spacjach; nazwy marek - wyłącznie po przecinkach, bo
        /// "bez marki" czy "John Deere" pocięte po spacji dawały wartość `bez`, której słownik nie zna.
        /// </summary>
        public static char[] MultiValueSeparators(string? parameterName) =>
            string.Equals(parameterName?.Trim(), "Numery katalogowe zamienników", StringComparison.OrdinalIgnoreCase)
                ? NumberSeparators
                : NameSeparators;

        /// <summary>Czy wartość jest poprawnym kodem GTIN (EAN-8, UPC-A, EAN-13, GTIN-14) z prawidłową sumą kontrolną.</summary>
        public static bool IsValidGtin(string? value)
        {
            var digits = (value ?? string.Empty).Trim();

            if (digits.Length is not (8 or 12 or 13 or 14) || !digits.All(char.IsAsciiDigit))
                return false;

            var sum = 0;

            // Wagi 3 i 1 naprzemiennie, licząc od cyfry tuż przed cyfrą kontrolną.
            for (int i = digits.Length - 2, weight = 3; i >= 0; i--, weight = 4 - weight)
                sum += (digits[i] - '0') * weight;

            return (10 - sum % 10) % 10 == digits[^1] - '0';
        }

        /// <summary>
        /// Czy EAN nadaje się na Allegro: poprawny GTIN spoza puli numerów ograniczonej dystrybucji (RCN).
        /// EAN-13 zaczynający się od 20-29 to numer wewnętrzny sklepu/dystrybutora - Allegro odrzuca
        /// z nim tworzenie produktu ("EAN (GTIN), który zaczyna się od 201 to ... RCN").
        /// </summary>
        public static bool IsPublicGtin(string? value)
        {
            if (!IsValidGtin(value))
                return false;

            var digits = value!.Trim();

            return !(digits.Length == 13 && digits[0] == '2');
        }

        /// <summary>"Liczba sztuk w opakowaniu" / "Liczba sztuk w zestawie" - sprzedajemy jedną pozycję.</summary>
        public static bool IsPackageCount(string? parameterName)
        {
            var name = parameterName?.Trim().ToLowerInvariant() ?? string.Empty;
            return name.StartsWith("liczba sztuk w ") && (name.EndsWith("opakowaniu") || name.EndsWith("zestawie"));
        }

        /// <summary>
        /// Parametry produktu dla oferty w danej kategorii: z kategorii oferty, gdy są wyliczone,
        /// inaczej z kategorii domyślnej, a dla starych danych bez kategorii - po jednym na nazwę. Parametrów
        /// z niezwiązanej kategorii nie zwraca.
        /// Parametry z innej kategorii Allegro odrzuca albo traktuje jako brak obowiązkowych.
        /// </summary>
        /// <param name="withOtherProductParameters">
        /// Dołóż parametry produktowe z pozostałych kategorii (bez powtórzeń nazw). Produkt z katalogu
        /// Allegro bywa w innej kategorii niż oferta i to jego kategoria decyduje o obowiązkowych
        /// parametrach produktu ("Marka maszyny" przy ofercie w kategorii, która jej nie ma).
        /// </param>
        public static List<ProductParameter> ForCategory(IEnumerable<ProductParameter> parameters, int offerCategoryId, int defaultCategoryId,
            bool withOtherProductParameters = false)
        {
            var all = parameters.ToList();

            var selected = all.Where(p => p.CategoryId != 0 && p.CategoryId == offerCategoryId).ToList();

            if (selected.Count == 0)
                selected = all.Where(p => p.CategoryId != 0 && p.CategoryId == defaultCategoryId).ToList();

            // Ani kategoria oferty, ani domyślna nie ma wyliczonych parametrów: zostają tylko stare wiersze bez
            // kategorii. Zestawu z niezwiązanej kategorii nie wysyłamy - Allegro odrzuca jej parametry, a krok
            // parametrów i tak wyliczy zestaw dla kategorii oferty i domyślnej w najbliższym cyklu.
            if (selected.Count == 0)
                return Distinct(all.Where(p => p.CategoryId == 0));

            if (withOtherProductParameters)
            {
                var names = new HashSet<string>(selected.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

                // Najpierw kategoria domyślna, potem pozostałe - pierwsza wartość danej nazwy wygrywa.
                var extra = all
                    .Where(p => p.IsForProduct && !names.Contains(p.Name))
                    .OrderBy(p => p.CategoryId == defaultCategoryId ? 0 : 1)
                    .ThenBy(p => p.CategoryId);

                selected.AddRange(Distinct(extra));
            }

            return selected;
        }

        private static List<ProductParameter> Distinct(IEnumerable<ProductParameter> parameters) => parameters
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        public static bool IsDictionary(CategoryParameter param) =>
            string.Equals(param.Type, "dictionary", StringComparison.OrdinalIgnoreCase) && param.Values.Count > 0;

        /// <summary>Klucz porównania: małe litery, bez polskich znaków i zbędnych spacji.</summary>
        internal static string Key(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var decomposed = value.Trim().ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);

            foreach (var ch in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }

            return string.Join(' ', sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
