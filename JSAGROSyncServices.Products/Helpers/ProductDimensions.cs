using JSAGROSyncServices.Contracts.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Gabaryty produktu odczytane ze specyfikacji dostawcy - jedyne miejsce, w którym serwis
    /// interpretuje wymiary. Korzysta z tego zarówno dobór cennika dostawy
    /// (<see cref="DeliveryMatcher"/>), jak i dopłata za produkt ponadgabarytowy, więc oba
    /// mechanizmy widzą ten sam produkt tak samo.
    ///
    /// Żaden dostawca nie podaje wymiarów w osobnych polach - są wyłącznie w specyfikacjach,
    /// pod różnymi nazwami i w różnych jednostkach.
    /// </summary>
    public static class ProductDimensions
    {
        /// <summary>Ile boków ma paczka - z dłuższej listy bierzemy największe.</summary>
        public const int SideCount = 3;

        /// <summary>
        /// Nazwa parametru z wymiarem: słowo bazowe i ewentualne dopowiedzenie. Z przodu dopuszczamy
        /// oznaczenie z rysunku technicznego ("C Szerokość zęba", "L - długość", "9. Wymiar frezów").
        /// </summary>
        private static readonly Regex DimensionName = new(
            @"^(?:[a-z0-9]{1,2}\s*[\.\)\-]\s*|[a-z]\s+)?(?<base>dlugosc|szerokosc|wysokosc|glebokosc|wymiary|wymiar)\b(?<qualifier>.*)$",
            RegexOptions.Compiled);

        /// <summary>Liczba w wartości parametru, np. "405 - 750" albo "90 x 100".</summary>
        private static readonly Regex Number = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        /// <summary>"1 200" to jedna liczba - bez tego wyszłyby dwie: 1 i 200.</summary>
        private static readonly Regex ThousandsSpace = new(@"(?<=\d)[\s ](?=\d{3}(?!\d))", RegexOptions.Compiled);

        /// <summary>
        /// Dopowiedzenia oznaczające gabaryt całego produktu. Wszystko inne to wymiar jakiegoś
        /// detalu ("szerokość zęba", "grubość ścianki rury", "długość kabla zasilającego")
        /// i do wyboru paczki się nie nadaje.
        /// </summary>
        private static readonly HashSet<string> WholeProductQualifiers = new(StringComparer.Ordinal)
        {
            string.Empty,
            "calkowita", "calkowity", "calkowite",
            "maksymalna", "maksymalny", "maksymalne", "maks", "max",
            "produktu", "opakowania", "paczki"
        };

        /// <summary>Oś, na której leży dany wymiar. Z jednej osi bierzemy największą podaną wartość.</summary>
        private enum Axis
        { Length, Width, Height }

        /// <summary>
        /// Boki produktu w centymetrach, od największego. Z każdej osi bierzemy największą podaną
        /// wartość ("Długość" i "Długość całkowita" to ten sam bok), a parametr zbiorczy
        /// ("Wymiary: 90 x 100") rozbijamy na osobne boki. Pusta lista oznacza, że dostawca
        /// nie podał żadnego gabarytu.
        /// </summary>
        public static IReadOnlyList<decimal> ReadCm(RolmarProduct product)
        {
            var byAxis = new Dictionary<Axis, decimal>();
            var loose = new List<decimal>();

            foreach (var specification in product.Specifications ?? new List<ProductSpecification>())
            {
                if (string.IsNullOrWhiteSpace(specification.Name))
                    continue;

                var match = DimensionName.Match(Normalize(specification.Name));

                if (!match.Success)
                    continue;

                var qualifier = match.Groups["qualifier"].Value.Trim(' ', ':', '.', '-');

                if (!WholeProductQualifiers.Contains(qualifier))
                    continue;

                var unit = UnitToCentimetres(specification.UnitName);

                if (unit == null)
                    continue;

                var values = ReadValues(specification.Value, unit.Value);

                if (values.Count == 0)
                    continue;

                var axis = match.Groups["base"].Value switch
                {
                    "szerokosc" => Axis.Width,
                    "wysokosc" => Axis.Height,
                    "dlugosc" or "glebokosc" => Axis.Length,
                    // "Wymiary: 90 x 100" - dostawca nie mówi, który bok jest który.
                    _ => (Axis?)null
                };

                if (axis == null)
                {
                    loose.AddRange(values);
                    continue;
                }

                // Zakres ("405 - 750") liczymy po największej wartości - paczka musi pomieścić
                // produkt w najgorszym przypadku.
                var value = values.Max();

                if (!byAxis.TryGetValue(axis.Value, out var current) || value > current)
                    byAxis[axis.Value] = value;
            }

            return byAxis.Values
                .Concat(loose)
                .OrderByDescending(v => v)
                .Take(SideCount)
                .ToList();
        }

        /// <summary>Najdłuższy bok produktu w centymetrach albo <c>null</c>, gdy nie podano wymiarów.</summary>
        public static decimal? LargestCm(RolmarProduct product)
        {
            var sides = ReadCm(product);

            return sides.Count == 0 ? null : sides[0];
        }

        /// <summary>
        /// Czy produkt jest ponadgabarytowy. Próg &lt;= 0 wyłącza sprawdzanie, a produkt
        /// bez podanych wymiarów nie jest uznawany za ponadgabarytowy - nie ma na to dowodu.
        /// </summary>
        public static bool IsOversized(RolmarProduct product, decimal thresholdCm) =>
            thresholdCm > 0 && LargestCm(product) > thresholdCm;

        /// <summary>Wszystkie liczby z wartości parametru, przeliczone na centymetry.</summary>
        private static List<decimal> ReadValues(string? value, decimal toCentimetres)
        {
            var result = new List<decimal>();

            if (string.IsNullOrWhiteSpace(value))
                return result;

            foreach (Match number in Number.Matches(ThousandsSpace.Replace(value, string.Empty)))
            {
                if (!decimal.TryParse(number.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    continue;

                var centimetres = parsed * toCentimetres;

                if (centimetres > 0)
                    result.Add(centimetres);
            }

            return result;
        }

        /// <summary>Nazwa parametru bez polskich znaków i wielkości liter - po tym dopasowujemy wzorzec.</summary>
        private static string Normalize(string name)
        {
            var builder = new StringBuilder(name.Length);

            foreach (var character in name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                // Ogonki i kreski są osobnymi znakami po dekompozycji - pomijamy je.
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                    continue;

                // "ł" nie ma postaci rozłożonej, więc trzeba je podmienić osobno.
                builder.Append(character == 'ł' ? 'l' : character);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Przelicznik jednostki na centymetry. <c>null</c> dla jednostek, które nie opisują gabarytu.
        ///
        /// Metry są pominięte celowo: w metrach dostawcy podają długość towaru nawiniętego lub
        /// w zwoju (wąż 30 m, lina 50 m, kabel 10 m), a nie wymiar przesyłki. Przeliczenie takiej
        /// wartości wyrzuciłoby produkt poza wszystkie cenniki i zakończyło jego ofertę.
        /// Sztywne przedmioty dostawcy podają w milimetrach lub centymetrach.
        /// </summary>
        private static decimal? UnitToCentimetres(string? unit) => unit?.Trim().ToLowerInvariant() switch
        {
            "mm" => 0.1m,
            "cm" => 1m,
            "dm" => 10m,
            "cal" or "\"" => 2.54m,
            _ => null
        };
    }
}
