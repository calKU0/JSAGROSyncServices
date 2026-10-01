using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Odczyt gabarytów ze specyfikacji dostawcy. Nazwy i formaty wartości pochodzą z danych
    /// produkcyjnych - w bazie jest ponad 1400 różnych nazw parametrów, a tylko część z nich
    /// opisuje rozmiar samej paczki.
    /// </summary>
    public class ProductDimensionsTests
    {
        [Theory]
        // Nazwa gola i z dopowiedzeniem oznaczajacym caly produkt.
        [InlineData("Długość", 60)]
        [InlineData("Długość całkowita", 60)]
        [InlineData("Długość maksymalna", 60)]
        [InlineData("Szerokość produktu", 60)]
        [InlineData("Wysokość opakowania", 60)]
        // Spacja na koncu nazwy - w bazie jest 646 takich wierszy ("Długość ").
        [InlineData("Długość ", 60)]
        // Oznaczenie z rysunku technicznego przed nazwa.
        [InlineData("C Szerokość", 60)]
        [InlineData("L - długość", 60)]
        public void Reads_whole_product_dimensions(string name, decimal expectedCm)
        {
            var product = TestProducts.Product(specifications: (name, "60", "cm"));

            Assert.Equal(expectedCm, Assert.Single(ProductDimensions.ReadCm(product)));
        }

        [Theory]
        // Wymiary detali - nie opisuja paczki, wiec nie moga decydowac o cenniku.
        [InlineData("Szerokość zęba")]
        [InlineData("C Szerokość zęba")]
        [InlineData("Szerokość wpustu")]
        [InlineData("Wysokość krzyżaka")]
        [InlineData("Grubość ścianki rury wewnętrznej")]
        [InlineData("Długość kabla zasilającego")]
        [InlineData("Długość włosia")]
        [InlineData("Szerokość robocza")]
        [InlineData("Średnica otworu")]
        [InlineData("Wysokość podnoszenia")]
        public void Ignores_component_dimensions(string name)
        {
            var product = TestProducts.Product(specifications: (name, "60", "cm"));

            Assert.Empty(ProductDimensions.ReadCm(product));
        }

        [Theory]
        [InlineData("600", "mm", 60)]
        [InlineData("60", "cm", 60)]
        [InlineData("6", "dm", 60)]
        [InlineData("10", "cal", 25.4)]
        public void Converts_units_to_centimetres(string value, string unit, decimal expectedCm)
        {
            var product = TestProducts.Product(specifications: ("Długość", value, unit));

            Assert.Equal(expectedCm, Assert.Single(ProductDimensions.ReadCm(product)));
        }

        [Theory]
        // Metry opisuja towar w zwoju (waz 30 m, lina 50 m), a nie wymiar przesylki.
        [InlineData("m")]
        [InlineData("mb")]
        // Jednostki, ktore w ogole nie sa dlugoscia.
        [InlineData("szt")]
        [InlineData("kg")]
        [InlineData("")]
        [InlineData(null)]
        public void Ignores_units_that_do_not_describe_a_parcel(string? unit)
        {
            var product = TestProducts.Product(specifications: ("Długość", "30", unit));

            Assert.Empty(ProductDimensions.ReadCm(product));
        }

        [Theory]
        // Zakres - paczka musi pomiescic produkt w najgorszym przypadku.
        [InlineData("405 - 750", 75)]
        [InlineData("955-985", 98.5)]
        // Dwie liczby w jednym polu.
        [InlineData("146 x 99", 14.6)]
        // Separator tysiecy: "1 200" to jedna liczba, nie 1 i 200.
        [InlineData("1 200", 120)]
        // Przecinek dziesietny.
        [InlineData("13,5", 1.35)]
        public void Reads_the_largest_number_from_the_value(string value, decimal expectedCm)
        {
            var product = TestProducts.Product(specifications: ("Długość", value, "mm"));

            Assert.Equal(expectedCm, ProductDimensions.LargestCm(product));
        }

        [Theory]
        [InlineData("[-]")]
        [InlineData("")]
        [InlineData("brak")]
        [InlineData("0")]
        public void Ignores_values_without_a_usable_number(string value)
        {
            var product = TestProducts.Product(specifications: ("Długość", value, "mm"));

            Assert.Empty(ProductDimensions.ReadCm(product));
        }

        [Fact]
        public void Takes_the_largest_value_per_axis()
        {
            // "Długość" i "Długość całkowita" to ten sam bok - nie wolno ich liczyć jako dwóch.
            var product = TestProducts.Product(specifications:
            [
                ("Długość", "60", "cm"),
                ("Długość całkowita", "90", "cm"),
                ("Szerokość", "40", "cm")
            ]);

            Assert.Equal(new decimal[] { 90, 40 }, ProductDimensions.ReadCm(product));
        }

        [Fact]
        public void Splits_a_combined_dimensions_parameter_into_sides()
        {
            var product = TestProducts.Product(specifications: ("Wymiary", "90 x 100", "mm"));

            Assert.Equal(new decimal[] { 10, 9 }, ProductDimensions.ReadCm(product));
        }

        [Fact]
        public void Returns_at_most_three_largest_sides()
        {
            var product = TestProducts.Product(specifications:
            [
                ("Długość", "100", "cm"),
                ("Szerokość", "80", "cm"),
                ("Wysokość", "60", "cm"),
                ("Wymiary", "40 x 20", "cm")
            ]);

            Assert.Equal(new decimal[] { 100, 80, 60 }, ProductDimensions.ReadCm(product));
        }

        [Fact]
        public void Returns_sides_sorted_from_the_largest()
        {
            var product = TestProducts.Product(specifications:
            [
                ("Wysokość", "30", "cm"),
                ("Długość", "90", "cm"),
                ("Szerokość", "50", "cm")
            ]);

            Assert.Equal(new decimal[] { 90, 50, 30 }, ProductDimensions.ReadCm(product));
        }

        [Fact]
        public void Product_without_specifications_has_no_dimensions()
        {
            Assert.Empty(ProductDimensions.ReadCm(TestProducts.Product()));
            Assert.Null(ProductDimensions.LargestCm(TestProducts.Product()));
        }

        [Theory]
        [InlineData(200, 150, true)]
        [InlineData(150, 150, false)]
        [InlineData(100, 150, false)]
        // Prog 0 wylacza doplate za gabaryt.
        [InlineData(500, 0, false)]
        public void Detects_oversized_products(decimal lengthCm, decimal thresholdCm, bool expected)
        {
            var product = TestProducts.Product(specifications: ("Długość", lengthCm.ToString(), "cm"));

            Assert.Equal(expected, ProductDimensions.IsOversized(product, thresholdCm));
        }

        [Fact]
        public void Product_without_dimensions_is_not_oversized()
        {
            // Brak wymiarów to brak dowodu na gabaryt - dopłaty nie doliczamy w ciemno.
            Assert.False(ProductDimensions.IsOversized(TestProducts.Product(weight: 50), 150m));
        }
    }
}
