using JSAGROSyncServices.Contracts.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Wymiary produktu odczytywane ze specyfikacji dostawcy. Rolmar nie podaje wymiarów w osobnych polach -
    /// są wyłącznie w specyfikacjach, z różnymi nazwami i jednostkami (mm, cm, m).
    /// </summary>
    public static class ProductDimensions
    {
        /// <summary>
        /// Nazwy opisujące rozmiar samego produktu. Świadomie pominięte są nazwy, które mimo słowa
        /// "długość"/"wysokość" nie mówią o gabarycie (np. głębokość zanurzenia, wysokość podnoszenia,
        /// długość kabla zasilającego, długość taśmy miarki).
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultDimensionNames =
        [
            "Długość",
            "Długość całkowita",
            "Długość maksymalna",
            "Szerokość",
            "Szerokość całkowita",
            "Wysokość",
            "Wysokość całkowita",
            "Głębokość",
            "Wymiary"
        ];

        private static readonly Regex Number = new(@"\d+(?:[.,]\d+)?", RegexOptions.Compiled);

        // "1 200" to jedna liczba - bez tego wyszłyby dwie: 1 i 200.
        private static readonly Regex ThousandsSpace = new(@"(?<=\d)[\s ](?=\d{3}(?!\d))", RegexOptions.Compiled);

        /// <summary>
        /// Największy wymiar produktu w centymetrach albo <c>null</c>, gdy żadna specyfikacja z listy
        /// nie daje się odczytać. Wartości typu "1200x800" czy "405 - 750" dają największą z liczb.
        /// </summary>
        public static decimal? GetLargestDimensionCm(IEnumerable<ProductSpecification>? specifications, IReadOnlyCollection<string> dimensionNames)
        {
            if (specifications == null || dimensionNames.Count == 0)
                return null;

            decimal? largest = null;

            foreach (var spec in specifications)
            {
                if (!dimensionNames.Contains(spec.Name?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    continue;

                var toCm = UnitToCentimeters(spec.UnitName);

                if (toCm == null || string.IsNullOrWhiteSpace(spec.Value))
                    continue;

                foreach (Match match in Number.Matches(ThousandsSpace.Replace(spec.Value, string.Empty)))
                {
                    if (!decimal.TryParse(match.Value.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                        continue;

                    var cm = value * toCm.Value;

                    if (largest == null || cm > largest)
                        largest = cm;
                }
            }

            return largest;
        }

        public static bool IsOversized(RolmarProduct product, decimal thresholdCm, IReadOnlyCollection<string> dimensionNames) =>
            GetLargestDimensionCm(product.Specifications, dimensionNames) > thresholdCm;

        /// <summary>
        /// Mnożnik do centymetrów; <c>null</c> dla jednostki, która nie opisuje gabarytu.
        /// Metry są pominięte celowo: w metrach dostawca podaje długość towaru nawiniętego lub w zwoju
        /// (żyłka 15 m, wąż 30 m), a nie wymiar przesyłki. Sztywne przedmioty mają wymiary w mm lub cm.
        /// </summary>
        private static decimal? UnitToCentimeters(string? unit) => unit?.Trim().ToLowerInvariant() switch
        {
            "mm" => 0.1m,
            "cm" => 1m,
            _ => null
        };
    }
}
