using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Dobór cennika dostawy. Zwrócony <c>null</c> ma konkretne znaczenie biznesowe: produktu
    /// nie wolno wystawić, a istniejącą ofertę trzeba zakończyć - dlatego każdy przypadek,
    /// w którym tu trafiamy, jest osobno opisany.
    /// </summary>
    public class DeliveryMatcherTests
    {
        [Fact]
        public void Oversized_delivery_type_goes_to_the_largest_price_list()
        {
            // Gabaryt jedzie osobnym transportem - wymiarów nie sprawdzamy w ogóle.
            var product = TestProducts.Product(weight: 1, deliveryType: 1);

            Assert.Equal("Duży", DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Fact]
        public void Oversized_delivery_type_ignores_small_dimensions()
        {
            var product = TestProducts.Product(1, 2, ("Długość", "10", "cm"));

            Assert.Equal("Duży", DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Theory]
        [InlineData(0.5, "Mały")]
        [InlineData(20, "Mały")]
        [InlineData(20.01, "Średni")]
        [InlineData(30, "Średni")]
        [InlineData(31.5, "Duży")]
        public void Without_dimensions_the_weight_decides(decimal weight, string expected)
        {
            var product = TestProducts.Product((float)weight);

            Assert.Equal(expected, DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Fact]
        public void Without_dimensions_and_weight_falls_back_to_the_largest_price_list()
        {
            // Nieznany rozmiar to częściej duża paczka niż mała.
            Assert.Equal("Duży", DeliveryMatcher.Match(TestProducts.Product(), TestProducts.Deliveries()));
        }

        [Fact]
        public void Component_dimensions_alone_count_as_no_dimensions()
        {
            // "Średnica otworu" nie mówi nic o paczce, więc zostaje sama waga.
            var product = TestProducts.Product(25, 0, ("Średnica otworu", "20", "mm"));

            Assert.Equal("Średni", DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Fact]
        public void Dimensions_decide_when_the_weight_is_unknown()
        {
            var product = TestProducts.Product(0, 0, ("Długość", "60", "cm"), ("Szerokość", "40", "cm"));

            Assert.Equal("Mały", DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Fact]
        public void Weight_over_the_limit_moves_the_product_to_the_next_price_list()
        {
            var small = TestProducts.Product(1, 0, ("Długość", "60", "cm"), ("Szerokość", "40", "cm"), ("Wysokość", "30", "cm"));
            var heavy = TestProducts.Product(25, 0, ("Długość", "60", "cm"), ("Szerokość", "40", "cm"), ("Wysokość", "30", "cm"));

            Assert.Equal("Mały", DeliveryMatcher.Match(small, TestProducts.Deliveries()));
            Assert.Equal("Średni", DeliveryMatcher.Match(heavy, TestProducts.Deliveries()));
        }

        [Fact]
        public void Too_heavy_for_every_price_list_cannot_be_published()
        {
            var product = TestProducts.Product(40, 0, ("Długość", "10", "cm"));

            Assert.Null(DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        [Fact]
        public void Too_large_for_every_price_list_cannot_be_published()
        {
            var product = TestProducts.Product(1, 0, ("Długość", "200", "cm"));

            Assert.Null(DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        // ------------------------------------------------------------ obrót paczki

        private static List<DeliverySettings> RotationList() =>
        [
            new() { Width = 310, Length = 210, Height = 100, Weight = 30m, DeliveryName = "Rotacja" }
        ];

        [Theory]
        // Produkt 300 x 200 mieści się w cenniku 310 x 210 niezależnie od tego,
        // który bok dostawca nazwał długością, a który szerokością.
        [InlineData("Długość", "300", "Szerokość", "200")]
        [InlineData("Szerokość", "300", "Długość", "200")]
        public void Parcel_may_be_rotated_to_fit(string firstName, string firstValue, string secondName, string secondValue)
        {
            var product = TestProducts.Product(1, 0, (firstName, firstValue, "cm"), (secondName, secondValue, "cm"));

            Assert.Equal("Rotacja", DeliveryMatcher.Match(product, RotationList()));
        }

        [Theory]
        // Jeden bok ponad limit wyklucza cały cennik - niezależnie który.
        [InlineData("300", "220")]
        [InlineData("320", "200")]
        [InlineData("320", "220")]
        public void A_single_side_over_the_limit_rejects_the_price_list(string length, string width)
        {
            var product = TestProducts.Product(1, 0, ("Długość", length, "cm"), ("Szerokość", width, "cm"));

            Assert.Null(DeliveryMatcher.Match(product, RotationList()));
        }

        [Fact]
        public void Third_side_over_the_limit_also_rejects_the_price_list()
        {
            var product = TestProducts.Product(1, 0,
                ("Długość", "300", "cm"), ("Szerokość", "200", "cm"), ("Wysokość", "150", "cm"));

            Assert.Null(DeliveryMatcher.Match(product, RotationList()));
        }

        // ------------------------------------------------------------ regresje

        [Fact]
        public void Length_in_metres_does_not_disqualify_a_coiled_product()
        {
            // Wąż 50 m to zwój, nie paczka o długości 5000 cm. Przeliczanie metrów
            // kończyło oferty 82 produktów Rolmaru.
            var hose = TestProducts.Product(8, 0, ("Długość", "50", "m"));

            Assert.Equal("Mały", DeliveryMatcher.Match(hose, TestProducts.Deliveries()));
        }

        [Fact]
        public void Trailing_space_in_the_parameter_name_is_tolerated()
        {
            var product = TestProducts.Product(1, 0, ("Długość ", "60", "cm"), ("Szerokość", "40", "cm"));

            Assert.Equal("Mały", DeliveryMatcher.Match(product, TestProducts.Deliveries()));
        }

        // ------------------------------------------------------------ konfiguracja

        [Fact]
        public void Without_configured_price_lists_nothing_can_be_published()
        {
            var empty = new List<DeliverySettings>();

            Assert.Null(DeliveryMatcher.Match(TestProducts.Product(1, 0, ("Długość", "10", "cm")), empty));
            Assert.Null(DeliveryMatcher.Match(TestProducts.Product(1, 1), empty));
            Assert.Null(DeliveryMatcher.Match(TestProducts.Product(), empty));
            Assert.Null(DeliveryMatcher.Largest(empty));
            Assert.Null(DeliveryMatcher.Match(TestProducts.Product(), null));
        }

        [Fact]
        public void Incomplete_price_list_entries_are_skipped()
        {
            // Wpis bez nazwy albo z zerowym wymiarem jest błędem konfiguracji - nie wolno
            // na niego nic wystawić, bo Allegro i tak odrzuci ofertę.
            var deliveries = new List<DeliverySettings>
            {
                new() { Width = 0, Length = 64, Height = 38, Weight = 20m, DeliveryName = "Bez szerokości" },
                new() { Width = 41, Length = 64, Height = 38, Weight = 20m, DeliveryName = "   " },
                new() { Width = 60, Length = 100, Height = 60, Weight = 30m, DeliveryName = "Poprawny" }
            };

            Assert.Equal("Poprawny", DeliveryMatcher.Match(TestProducts.Product(1), deliveries));
            Assert.Equal("Poprawny", DeliveryMatcher.Largest(deliveries));
        }

        [Fact]
        public void Price_lists_are_tried_from_the_smallest_regardless_of_their_order()
        {
            var shuffled = new List<DeliverySettings>
            {
                new() { Width = 60, Length = 150, Height = 60, Weight = 31.5m, DeliveryName = "Duży" },
                new() { Width = 41, Length = 64, Height = 38, Weight = 20m, DeliveryName = "Mały" },
                new() { Width = 60, Length = 100, Height = 60, Weight = 30m, DeliveryName = "Średni" }
            };

            Assert.Equal("Mały", DeliveryMatcher.Match(TestProducts.Product(1), shuffled));
            Assert.Equal("Duży", DeliveryMatcher.Largest(shuffled));
        }

        // ------------------------------------------------------------ Allegro Smart

        [Fact]
        public void MatchDelivery_returns_the_whole_price_list_entry()
        {
            // Cena oferty zależy nie tylko od nazwy cennika, ale też od tego, czy jest w Smart.
            var deliveries = TestProducts.Deliveries();
            var matched = DeliveryMatcher.MatchDelivery(TestProducts.Product(1), deliveries);

            Assert.NotNull(matched);
            Assert.Equal("Mały", matched!.DeliveryName);
            Assert.True(matched.IsSmart);
        }

        [Fact]
        public void MatchDelivery_carries_the_smart_flag_of_the_matched_list()
        {
            // Układ z produkcji Gąski: trzy cenniki Smart i "JAG API" jako łapanka na resztę.
            var deliveries = TestProducts.Deliveries();
            deliveries.Add(new DeliverySettings
            {
                Width = 999999,
                Length = 999999,
                Height = 999999,
                Weight = 999999m,
                DeliveryName = "JAG API",
                IsSmart = false
            });

            var small = DeliveryMatcher.MatchDelivery(TestProducts.Product(1), deliveries);
            var heavy = DeliveryMatcher.MatchDelivery(TestProducts.Product(100), deliveries);

            Assert.Equal("Mały", small!.DeliveryName);
            Assert.True(small.IsSmart);

            Assert.Equal("JAG API", heavy!.DeliveryName);
            Assert.False(heavy.IsSmart);
        }

        [Fact]
        public void MatchDelivery_returns_null_when_nothing_fits()
        {
            Assert.Null(DeliveryMatcher.MatchDelivery(TestProducts.Product(40), TestProducts.Deliveries()));
        }

        [Fact]
        public void Match_and_MatchDelivery_agree()
        {
            var deliveries = TestProducts.Deliveries();

            foreach (var product in new[]
            {
                TestProducts.Product(1),
                TestProducts.Product(25),
                TestProducts.Product(40),
                TestProducts.Product(1, 1),
                TestProducts.Product(),
                TestProducts.Product(1, 0, ("Długość", "200", "cm"))
            })
            {
                Assert.Equal(
                    DeliveryMatcher.Match(product, deliveries),
                    DeliveryMatcher.MatchDelivery(product, deliveries)?.DeliveryName);
            }
        }

        [Fact]
        public void IsLargest_reports_only_products_in_the_biggest_price_list()
        {
            var deliveries = TestProducts.Deliveries();

            Assert.True(DeliveryMatcher.IsLargest(TestProducts.Product(1, 1), deliveries));
            Assert.True(DeliveryMatcher.IsLargest(TestProducts.Product(31), deliveries));
            Assert.False(DeliveryMatcher.IsLargest(TestProducts.Product(1), deliveries));
            // Produkt, który nigdzie się nie mieści, nie jest "w największym cenniku".
            Assert.False(DeliveryMatcher.IsLargest(TestProducts.Product(40), deliveries));
        }
    }
}
