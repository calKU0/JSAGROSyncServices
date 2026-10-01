using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Parametry, których dostawcy nie podają, a Allegro wymaga. Wartość spoza słownika
    /// kategorii Allegro odrzuca razem z całą ofertą, więc dobór musi wychodzić z listy
    /// dopuszczalnych wartości, a nie z naszych założeń.
    /// </summary>
    public class ParameterDefaultsTests
    {
        [Theory]
        // Nazwy z realnych kategorii części.
        [InlineData("Liczba tarcz w ofercie")]
        [InlineData("Liczba sztuk w ofercie")]
        [InlineData("Liczba klocków w ofercie")]
        [InlineData("liczba tarcz w ofercie")]
        [InlineData("  Liczba tarcz w ofercie  ")]
        public void Count_in_offer_is_recognised(string name)
        {
            Assert.True(ParameterDefaults.IsCountInOffer(name));
        }

        [Theory]
        // Parametry o liczbie, ale nie o zawartości oferty - tych nie wolno ustawiać na 1.
        [InlineData("Liczba biegów")]
        [InlineData("Liczba zębów")]
        [InlineData("Liczba cylindrów")]
        [InlineData("Pojemność w ofercie")]
        [InlineData("Strona zabudowy")]
        [InlineData("")]
        [InlineData(null)]
        public void Other_parameters_are_not_treated_as_a_count(string? name)
        {
            Assert.False(ParameterDefaults.IsCountInOffer(name));
        }

        [Fact]
        public void Count_in_offer_is_always_one()
        {
            Assert.Equal("1", ParameterDefaults.CountInOfferValue);
        }

        [Fact]
        public void Universal_value_is_picked_from_the_category_dictionary()
        {
            // Układ z produkcji: 14 kategorii dopuszcza "uniwersalne" obok wartości kierunkowych.
            string?[] allowed = ["przód", "tył", "przód + tył", "uniwersalne", "lewa", "prawa"];

            Assert.Equal("uniwersalne", ParameterDefaults.UniversalMountingSide(allowed));
        }

        [Fact]
        public void Both_sides_is_used_when_there_is_no_universal_value()
        {
            string?[] allowed = ["przód", "tył", "przód + tył"];

            Assert.Equal("przód + tył", ParameterDefaults.UniversalMountingSide(allowed));
        }

        [Fact]
        public void Dictionary_spelling_is_preserved()
        {
            // Allegro porównuje wartości dosłownie, więc oddajemy je tak, jak podaje słownik.
            string?[] allowed = ["Uniwersalne"];

            Assert.Equal("Uniwersalne", ParameterDefaults.UniversalMountingSide(allowed));
        }

        [Fact]
        public void No_side_is_invented_when_only_directions_are_allowed()
        {
            // Tak wygląda słownik w kategoriach, gdzie parametr jest wymagany - zmyślony
            // "przód" na części tylnej wprowadzałby kupującego w błąd, więc zostawiamy pusto.
            string?[] allowed = ["przód", "tył"];

            Assert.Null(ParameterDefaults.UniversalMountingSide(allowed));
        }

        [Fact]
        public void Empty_dictionary_gives_no_value()
        {
            Assert.Null(ParameterDefaults.UniversalMountingSide(null));
            Assert.Null(ParameterDefaults.UniversalMountingSide([]));
            Assert.Null(ParameterDefaults.UniversalMountingSide([null, "", "   "]));
        }

        [Fact]
        public void Preference_order_decides_between_several_allowed_values()
        {
            string?[] allowed = ["dowolna", "przód + tył", "uniwersalne"];

            Assert.Equal("uniwersalne", ParameterDefaults.UniversalMountingSide(allowed));
        }
    }
}
