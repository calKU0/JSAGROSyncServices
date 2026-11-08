using System.Globalization;
using System.Text;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Klasa wysyłki podawana przez dostawcę (Rolmar: pole "cubature"). Specyfikacje Rolmara rzadko
    /// mają komplet wymiarów, za to prawie każdy produkt ma klasę: gabaryt paczkomatu InPost
    /// (A/B/C), zwykłą paczkę kurierską albo transport paletowy / dłużycę.
    /// </summary>
    public static class ShippingClasses
    {
        // Gabaryty skrytek paczkomatu InPost w cm, od najdłuższego boku.
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<decimal>> LockerSizes =
            new Dictionary<string, IReadOnlyList<decimal>>(StringComparer.Ordinal)
            {
                ["inpost_a"] = new[] { 64m, 38m, 8m },
                ["inpost_b"] = new[] { 64m, 38m, 19m },
                ["inpost_c"] = new[] { 64m, 41m, 38m }
            };

        private static readonly HashSet<string> Freight = new(StringComparer.Ordinal)
        {
            "polpaleta", "paleta", "dluzyca"
        };

        /// <summary>Boki paczki (cm, malejąco) dla klasy paczkomatowej; dla pozostałych - null.</summary>
        public static IReadOnlyList<decimal>? ParcelSidesCm(string? shippingClass) =>
            LockerSizes.TryGetValue(Normalize(shippingClass), out var sides) ? sides : null;

        /// <summary>Towar jedzie paletą albo jako dłużyca - nie mieści się w paczce kurierskiej.</summary>
        public static bool IsFreight(string? shippingClass) => Freight.Contains(Normalize(shippingClass));

        /// <summary>Zwykła paczka kurierska - bez gabarytu paczkomatu, ale też nie paleta.</summary>
        public static bool IsCourierParcel(string? shippingClass) => Normalize(shippingClass) == "paczka";

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder();

            foreach (var c in value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                sb.Append(c == 'ł' ? 'l' : c);
            }

            return sb.ToString();
        }
    }
}
