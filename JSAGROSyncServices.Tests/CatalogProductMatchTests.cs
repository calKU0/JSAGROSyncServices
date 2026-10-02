using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Services.Allegro;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Potwierdzanie, że produkt znaleziony w katalogu Allegro to faktycznie nasz towar.
    /// Błąd w tę stronę jest kosztowny: oferta tarczy hamulcowej trafiła pod produkt
    /// "KORALIKI SZKLANE 60szt JABLONEX 11119001", bo numer katalogowy "11119001" zgadzał się
    /// z parametrem "Kod producenta" koralików.
    /// </summary>
    public class CatalogProductMatchTests
    {
        private static SearchProdustsResponse.Product Candidate(string name, params (string Parameter, string Value)[] parameters) => new()
        {
            Id = "cafe0000-0000-0000-0000-000000000001",
            Name = name,
            Category = new SearchProdustsResponse.Category { Id = "250406" },
            Parameters = parameters
                .Select(p => new SearchProdustsResponse.Parameter { Name = p.Parameter, Values = [p.Value] })
                .ToList()
        };

        private static RolmarProduct Product(string? brand) => new()
        {
            Id = 1,
            Code = "H17ZKO",
            Name = "Tarcza hamulcowa",
            SupplierName = brand
        };

        private static HashSet<string> Identifiers(params string[] values) =>
            values.Select(AllegroProductService.Normalize).ToHashSet(StringComparer.Ordinal);

        // ------------------------------------------------------------ regresja z produkcji

        [Fact]
        public void A_digits_only_number_alone_does_not_confirm_a_match()
        {
            var beads = Candidate("KORALIKI SZKLANE 60szt JABLONEX 11119001", ("Kod producenta", "11119001"));

            // Numer się zgadza, więc samo Matches przechodzi...
            Assert.True(AllegroProductService.Matches(beads, Identifiers("11119001"), primaryOnly: true));

            // ...ale marka nie, więc trafienie nie jest potwierdzone.
            Assert.False(AllegroProductService.Confirms(beads, Identifiers("11119001"), Product("SRP")));
        }

        [Fact]
        public void A_digits_only_number_confirms_when_the_brand_agrees()
        {
            var disc = Candidate("Tarcza hamulcowa SRP 11119001", ("Kod producenta", "11119001"), ("Marka", "SRP"));

            Assert.True(AllegroProductService.Confirms(disc, Identifiers("11119001"), Product("SRP")));
        }

        [Fact]
        public void Brand_in_the_catalog_name_is_enough_to_confirm()
        {
            // Katalog Allegro często nie ma parametru marki, ale ma ją w nazwie.
            var disc = Candidate("Tarcza hamulcowa SRP 11119001", ("Kod producenta", "11119001"));

            Assert.True(AllegroProductService.Confirms(disc, Identifiers("11119001"), Product("SRP")));
        }

        [Fact]
        public void Without_our_brand_a_digits_only_match_is_rejected()
        {
            var beads = Candidate("KORALIKI SZKLANE 60szt JABLONEX 11119001", ("Kod producenta", "11119001"));

            Assert.False(AllegroProductService.Confirms(beads, Identifiers("11119001"), Product(null)));
        }

        [Fact]
        public void A_short_number_with_a_letter_does_not_confirm_on_its_own()
        {
            // Z produkcji: "279-V" to u nas pierścień SBP, a w katalogu Allegro lakier do włosów.
            // Sama litera w numerze niczego nie dowodzi, jeśli numer jest krótki.
            var hairspray = Candidate("HELEN SEWARD QUICK&EASY LAKIER MOCNY DO WŁOSÓW 300ml",
                ("Kod producenta", "279-V"));
            var ours = new RolmarProduct
            {
                Id = 1,
                Code = "H1CCVC",
                Name = "Pierścień sworznia szczęki hamulcowej",
                SupplierName = "SBP"
            };

            Assert.True(AllegroProductService.Matches(hairspray, Identifiers("279-V"), primaryOnly: true));
            Assert.False(AllegroProductService.Confirms(hairspray, Identifiers("279-V"), ours));
        }

        [Theory]
        // Granica: numer z literą potwierdza sam dopiero od sześciu znaków.
        [InlineData("279V", false)]
        [InlineData("AG159", false)]
        [InlineData("AG1592", true)]
        [InlineData("STR15A359", true)]
        // Same cyfry nie potwierdzają nigdy - kody numeryczne powtarzają się między branżami.
        [InlineData("11119001", false)]
        [InlineData("070696", false)]
        public void Only_a_long_enough_number_with_a_letter_stands_alone(string identifier, bool expected)
        {
            Assert.Equal(expected, AllegroProductService.IsStrongIdentifier(identifier));
        }

        // ------------------------------------------------------------ numery z literami

        [Theory]
        // Takie numery rozróżniają same z siebie - marki nie wymagamy.
        [InlineData("STR-15A359")]
        [InlineData("ZK8021 LBPU312-31-22BBK")]
        [InlineData("L166731-JD")]
        public void A_number_with_letters_confirms_on_its_own(string number)
        {
            var part = Candidate("Zaczep transportowy", ("Numer katalogowy części", number));

            Assert.True(AllegroProductService.Confirms(part, Identifiers(number), Product("ANAC MAKINA")));
        }

        [Fact]
        public void A_number_with_letters_found_in_the_name_also_confirms()
        {
            var part = Candidate("STR-15A331 Ramię podnośnika G931870030012 FEND");

            Assert.True(AllegroProductService.Confirms(part, Identifiers("STR-15A331"), Product("S-TR")));
        }

        // ------------------------------------------------------------ próg długości

        // ------------------------------------------------------------ zgodnosc nazw

        [Fact]
        public void Matching_names_confirm_when_the_brand_is_absent_from_the_name()
        {
            // Z produkcji: numer 11051259 w nazwie, marki SRP w niej nie ma, ale oba
            // opisy mowia o zawieszeniu - to ten sam towar.
            var bushing = Candidate("TULEJA ZAWIESZENIA VOLVO WOZIDŁO 11051259", ("Kod producenta", "11051259"));
            var ours = new RolmarProduct { Id = 1, Code = "H0H6PI", Name = "Pozostałe elementy zawieszenia TUZ", SupplierName = "SRP" };

            Assert.True(AllegroProductService.Confirms(bushing, Identifiers("11051259"), ours));
        }

        [Fact]
        public void Unrelated_names_do_not_confirm()
        {
            var beads = Candidate("KORALIKI SZKLANE 60szt JABLONEX 11119001", ("Kod producenta", "11119001"));

            Assert.False(AllegroProductService.Confirms(beads, Identifiers("11119001"), Product("SRP")));
        }

        [Theory]
        [InlineData("TULEJA ZAWIESZENIA VOLVO", "Pozostałe elementy zawieszenia TUZ", true)]
        [InlineData("Tarcza hamulcowa SRP", "Tarcza hamulcowa", true)]
        // Krótkie słowa pasują do wszystkiego, więc się nie liczą.
        [InlineData("Lampion MyHome metal 13,5 cm", "Tarcza hamulcowa", false)]
        [InlineData("KORALIKI SZKLANE JABLONEX", "Tarcza hamulcowa", false)]
        [InlineData("Zestaw do VOLVO", "Zestaw do CAT", false)]
        [InlineData("", "Tarcza hamulcowa", false)]
        public void Shared_word_decides_whether_the_names_agree(string catalogName, string ourName, bool expected)
        {
            Assert.Equal(expected, AllegroProductService.SharesMeaningfulWord(catalogName, ourName));
        }

        [Theory]
        // Numer z literami wystarczy od 4 znaków.
        [InlineData("AG12", true)]
        [InlineData("STR15A359", true)]
        // Same cyfry muszą być dłuższe - "480" podmieniło przenośnik na zacisk hamulca.
        [InlineData("480", false)]
        [InlineData("1234", false)]
        [InlineData("12345", false)]
        [InlineData("070696", true)]
        [InlineData("11119001", true)]
        // Za krótkie, żeby cokolwiek identyfikować.
        [InlineData("L", false)]
        [InlineData("24", false)]
        public void Only_distinctive_numbers_are_used_as_identifiers(string identifier, bool expected)
        {
            Assert.Equal(expected, AllegroProductService.IsUsableIdentifier(identifier));
        }
    }
}
