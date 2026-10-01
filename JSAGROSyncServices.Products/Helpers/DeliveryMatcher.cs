using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Wybór cennika dostawy dla produktu - wspólny dla wszystkich dostawców.
    ///
    /// Zasady:
    /// <list type="number">
    /// <item>produkt ponadgabarytowy / niestandardowy (<c>DeliveryType != 0</c>) idzie w największy cennik,</item>
    /// <item>pozostałe dobieramy po wadze i wymiarach - od najmniejszego cennika do największego,</item>
    /// <item>cennik odpada, gdy przekroczona jest waga albo choć jeden wymiar,</item>
    /// <item>bez wymiarów decyduje sama waga, a bez wagi i wymiarów - największy cennik,</item>
    /// <item>produkt, który nie mieści się nigdzie, nie nadaje się do wystawienia.</item>
    /// </list>
    ///
    /// Wymiary dopasowujemy z obrotem paczki: produkt 300x200 cm zmieści się w cenniku
    /// 310x210 cm niezależnie od tego, który bok dostawca nazwał długością. Sprowadza się to
    /// do posortowania obu trójek malejąco i porównania ich po kolei.
    ///
    /// Same wymiary czyta <see cref="ProductDimensions"/> - ten sam parser, co dopłata za gabaryt.
    /// </summary>
    public static class DeliveryMatcher
    {
        /// <summary>
        /// Nazwa cennika dostawy dla produktu albo <c>null</c>, gdy żaden nie pasuje - czyli produkt
        /// jest cięższy albo większy niż największy cennik. <c>null</c> oznacza, że produktu nie wolno
        /// wystawić, a istniejącą ofertę trzeba zakończyć.
        /// </summary>
        public static string? Match(RolmarProduct product, IReadOnlyList<DeliverySettings>? deliveries) =>
            MatchDelivery(product, deliveries)?.DeliveryName;

        /// <summary>
        /// Cały wpis cennika dopasowanego do produktu - poza nazwą potrzebna jest też informacja,
        /// czy cennik jest objęty Smart, bo od tego zależy doliczenie kosztu wysyłki do ceny.
        /// </summary>
        public static DeliverySettings? MatchDelivery(RolmarProduct product, IReadOnlyList<DeliverySettings>? deliveries)
        {
            var available = Usable(deliveries);

            if (available.Count == 0)
                return null;

            // Gabaryt i towar niestandardowy jadą osobnym transportem - wymiarów nie sprawdzamy,
            // bo i tak nie zmieściłyby się w żadnej zwykłej paczce.
            if (product.DeliveryType != 0)
                return available[^1];

            var sides = ProductDimensions.ReadCm(product);
            var weight = (decimal)product.Weight;

            // Dostawca nie podał ani gabarytów, ani wagi - nie ma czego mierzyć, więc zakładamy
            // najgorszy przypadek. Nieznany rozmiar to częściej duża paczka niż mała.
            if (sides.Count == 0 && weight <= 0)
                return available[^1];

            foreach (var delivery in available)
            {
                if (delivery.Weight < weight)
                    continue;

                // Bez wymiarów zostaje sama waga - innego kryterium nie mamy.
                if (sides.Count == 0 || Fits(sides, delivery))
                    return delivery;
            }

            return null;
        }

        /// <summary>Największy cennik - tu trafiają produkty ponadgabarytowe.</summary>
        public static string? Largest(IReadOnlyList<DeliverySettings>? deliveries)
        {
            var available = Usable(deliveries);

            return available.Count == 0 ? null : available[^1].DeliveryName;
        }

        /// <summary>Czy produkt trafia w największy cennik - od tego zależy dopłata za dropshipping.</summary>
        public static bool IsLargest(RolmarProduct product, IReadOnlyList<DeliverySettings>? deliveries)
        {
            var largest = Largest(deliveries);

            return largest != null && string.Equals(Match(product, deliveries), largest, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Cenniki od najmniejszego do największego - w tej kolejności szukamy pasującego.</summary>
        private static List<DeliverySettings> Usable(IReadOnlyList<DeliverySettings>? deliveries) =>
            (deliveries ?? Array.Empty<DeliverySettings>())
                .Where(d => !string.IsNullOrWhiteSpace(d.DeliveryName) && d.Weight > 0
                            && d.Width > 0 && d.Length > 0 && d.Height > 0)
                .OrderBy(d => d.Weight)
                .ThenBy(d => d.Length * d.Width * d.Height)
                .ToList();

        /// <summary>
        /// Czy paczka o podanych bokach zmieści się w cenniku. Oba zestawy sortujemy malejąco
        /// i porównujemy po kolei - to odpowiada obróceniu paczki najkorzystniejszą stroną.
        /// Wystarczy jeden bok ponad limit, żeby cennik odpadł.
        /// </summary>
        private static bool Fits(IReadOnlyList<decimal> sides, DeliverySettings delivery)
        {
            var limits = new[] { delivery.Width, delivery.Length, delivery.Height };

            Array.Sort(limits);
            Array.Reverse(limits);

            if (sides.Count > limits.Length)
                return false;

            // ProductDimensions zwraca boki już posortowane malejąco.
            for (var i = 0; i < sides.Count; i++)
            {
                if (sides[i] > limits[i])
                    return false;
            }

            return true;
        }
    }
}
