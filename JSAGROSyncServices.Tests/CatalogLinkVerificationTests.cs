using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Services.Allegro;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Weryfikacja istniejących powiązań z katalogiem Allegro na przypadkach z produkcji. Odstraszacz na krety
    /// KR10 stał pod radiem Blaupunkt "KR 10 WH"; większość starych powiązań jest jednak poprawna i nie wolno
    /// ich odpinać (odpięcie kończy ofertę).
    /// </summary>
    public class CatalogLinkVerificationTests
    {
        private static SearchProdustsResponse.Product Catalog(string name, params (string Parameter, string Value)[] parameters) => new()
        {
            Id = "cafe0000-0000-0000-0000-000000000002",
            Name = name,
            Category = new SearchProdustsResponse.Category { Id = "259425" },
            Parameters = parameters.Select(p => new SearchProdustsResponse.Parameter { Name = p.Parameter, Values = [p.Value] }).ToList()
        };

        private static RolmarProduct Product(string code, string name, string? brand = null, string? ean = null) => new()
        {
            Id = 1,
            Code = code,
            Name = name,
            SupplierName = brand,
            Ean = ean
        };

        private static bool Plausible(RolmarProduct product, SearchProdustsResponse.Product catalog) =>
            AllegroProductService.IsPlausibleLink(catalog, AllegroProductService.ProductIdentifiers(product, IntegrationCompany.Rolmar), product);

        private static bool Confirmed(RolmarProduct product, SearchProdustsResponse.Product catalog) =>
            AllegroProductService.IsConfirmedMatch(catalog, AllegroProductService.ProductIdentifiers(product, IntegrationCompany.Rolmar), product);

        private static readonly RolmarProduct Kr10 = Product("KR10", "Naturalny odstraszacz na krety KUNAGONE ( sprzedawane po 10 )");

        [Fact]
        public void Short_code_glued_from_name_words_is_not_a_match()
        {
            var radio = Catalog("Radio sieciowe AM, FM Blaupunkt KR 10 WH",
                ("EAN (GTIN)", "4056591000574"), ("Model", "KR 10 WH"), ("Kod producenta", "KRC010"));

            Assert.False(Confirmed(Kr10, radio));
            Assert.False(Plausible(Kr10, radio));
        }

        [Fact]
        public void The_right_catalog_product_for_kr10_is_accepted()
        {
            var repellent = Catalog("Naturalny odstraszacz na krety KUNAGONE ( sprzedawane po 10 ) KR10",
                ("Kod producenta", "KR10"), ("Model", "KR10"), ("EAN (GTIN)", "6971050264701"));

            Assert.True(Confirmed(Kr10, repellent));
        }

        [Theory]
        [InlineData("Przedłużacz 5 gniazd z wyłącznikiem 5m", "Przedłużacz 5 gniazd z wyłącznikiem 3m")]
        [InlineData("Kolanko wtykowe tekalan (90 stopni) z gwintem zewnętrznym (teflon) 10mm x", "Kolanko wtykowe tekalan (90 stopni) z gwintem zewnętrznym (teflon) 12mm x 3")]
        [InlineData("Pokrętło do nasadek TYP L 3/4\" 400 mm, CR-V, TEGER", "Pokrętło do nasadek TYP L 3/4\" 450 mm / TEGER")]
        public void A_different_size_is_a_different_product(string ours, string theirs)
        {
            Assert.True(AllegroProductService.HasConflictingMeasures(ours, theirs));
            Assert.False(Plausible(Product("X-1", ours), Catalog(theirs)));
        }

        [Fact]
        public void Code_fragments_are_not_measures()
        {
            // "/3L" to końcówka kodu, a nie 3 litry - nie kłóci się z "40L".
            Assert.False(AllegroProductService.HasConflictingMeasures(
                "Rozdzielacz hydrauliczny 3-sekcyjny max przepływ 40L sterowany na linkę",
                "Waryński W-ROZ40L/3L rozdzielacz hydrauliczny sekcje linka waryński"));
        }

        [Fact]
        public void Different_eans_mean_different_products()
        {
            var product = Product("W-073609", "Płoza długa obracalna Kverneland stal borowa Waryński", "WARYNSKI", "5902287241372");
            var catalog = Catalog("Płoza długa obracalna Kverneland stal borowa Waryński", ("EAN (GTIN)", "5902287024296"));

            Assert.True(AllegroProductService.HasConflictingEan(catalog, product));
            Assert.False(Plausible(product, catalog));
        }

        [Fact]
        public void Gtin14_with_leading_zero_equals_ean13()
        {
            var product = Product("A-1", "Łożysko", ean: "5902287241372");

            Assert.False(AllegroProductService.HasConflictingEan(Catalog("Łożysko", ("EAN (GTIN)", "05902287241372")), product));
        }

        [Theory]
        // Poprawne stare powiązania, których reguły nowego dopasowania nie potwierdzają - nie wolno ich odpiąć.
        [InlineData("5040030570", "Rolka napinacza Bizon.", "Rolka napinająca koło pasowe Bizon 5040030570")]
        [InlineData("5644730061", "Bęben sprzęgła kpl. Anna", "Bęben sprzęgła kpl. Anna")]
        [InlineData("80455006U", "Widełki lewe podnośnika C-385 ORYGINAŁ URSUS", "Widełki Lewe C-385 80455006")]
        [InlineData("W-25-1SN-PPG25", "Wąż hydrauliczny do zakuwania POWER-PLUS Exceed EN 853 1SN DN25 1-oplotowy",
            "Wąż hydrauliczny do zakuwania POWER-PLUS Exceed EN 853 1SN DN25 1-oplotowy")]
        public void Correct_legacy_links_stay(string code, string ours, string theirs)
        {
            Assert.True(Plausible(Product(code, ours), Catalog(theirs)));
        }

        [Fact]
        public void Exact_catalog_number_keeps_a_link_with_a_different_name()
        {
            var catalog = Catalog("KRÓCIEC SEKCJI ROZDZIELACZA OPRYSKIWACZA", ("Numer katalogowy części", "4031090240"));

            Assert.True(Plausible(Product("4031090240", "Nasadka złącza Opryskiwacz ( sprzedawane po 10 )"), catalog));
        }

        private static RolmarProduct Alternator44A()
        {
            var product = Product("EX-260000EX", "Alternator kompletny C-360 EXPOM EXPOM KWIDZYN eu", "EXPOM", "5902287194333");

            product.Specifications =
            [
                new ProductSpecification { Name = "Opakowanie", Value = "1", UnitName = "szt" },
                new ProductSpecification { Name = "Napięcie", Value = "14", UnitName = "V" },
                new ProductSpecification { Name = "Moc", Value = "44", UnitName = "A" }
            ];

            return product;
        }

        private static SearchProdustsResponse.Product WithDescription(SearchProdustsResponse.Product catalog, string html)
        {
            catalog.Description = new SearchProdustsResponse.Description
            {
                Sections = [new SearchProdustsResponse.Section { Items = [new SearchProdustsResponse.Item { Type = "TEXT", Content = html }] }]
            };

            return catalog;
        }

        [Fact]
        public void Catalog_product_with_our_ean_but_other_current_in_description_is_unlinked()
        {
            // Produkt z katalogu ma nasz EAN i numer, ale w opisie 70A - nasz alternator ma 44 A.
            var catalog = WithDescription(
                Catalog("Expom EX260000EX alternator ursus expom",
                    ("Numer katalogowy części", "EX260000EX"), ("Producent części", "Expom"), ("EAN (GTIN)", "5902287194333")),
                "<p><b>Alternator EX-260000 C-360 14V 70A Nowy Typ EXPOM</b></p><p>✅&nbsp;Moc:&nbsp;70A ✅&nbsp;Volt:&nbsp;14</p>");

            Assert.True(AllegroProductService.HasConflictingSpecifications(catalog, Alternator44A()));
            Assert.False(Confirmed(Alternator44A(), catalog));
            Assert.False(Plausible(Alternator44A(), catalog));
            Assert.Equal("inna specyfikacja: nasze 44 A, w katalogu 70 A",
                AllegroProductService.LinkProblem(catalog, AllegroProductService.ProductIdentifiers(Alternator44A(), IntegrationCompany.Rolmar), Alternator44A()));
        }

        [Fact]
        public void Catalog_product_with_our_current_is_accepted()
        {
            var catalog = Catalog("Alternator C-360 44A Expom Kwidzyń EX-260000EX",
                ("Numer katalogowy części", "EX-260000EX"), ("Producent części", "Waryński"));

            Assert.False(AllegroProductService.HasConflictingSpecifications(catalog, Alternator44A()));
            Assert.True(Confirmed(Alternator44A(), catalog));
        }

        [Fact]
        public void Charging_voltage_counts_as_the_system_voltage()
        {
            // "Napięcie: 12V" w opisie i 14 V w specyfikacji to ten sam alternator.
            var catalog = WithDescription(Catalog("Alternator C-360", ("Numer katalogowy części", "EX-260000EX")),
                "<p>Napięcie:12V Opakowanie:1szt</p>");

            Assert.False(AllegroProductService.HasConflictingSpecifications(catalog, Alternator44A()));
        }

        [Fact]
        public void Specification_decides_nothing_when_catalog_does_not_state_the_unit()
        {
            var catalog = WithDescription(Catalog("Alternator C-360 EXPOM", ("Waga produktu z opakowaniem jednostkowym", "10,000 kg")),
                "<p>Waga: 4,62 kg. Pasuje do C-355, C-360.</p>");

            Assert.False(AllegroProductService.HasConflictingSpecifications(catalog, Alternator44A()));
            Assert.False(AllegroProductService.HasConflictingSpecifications(catalog, Product("EX-260000EX", "Alternator")));
        }

        [Fact]
        public void Different_current_in_names_is_a_different_variant()
        {
            Assert.True(AllegroProductService.HasConflictingMeasures("Alternator C-360 44A", "Alternator kompletny URSUS C-360 72A EXPOM"));
            Assert.False(AllegroProductService.HasConflictingMeasures("Alternator C-360 72A", "Alternator kompletny URSUS C-360 72A EXPOM"));
        }
    }
}
