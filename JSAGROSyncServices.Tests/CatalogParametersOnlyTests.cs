using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Services.Allegro;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Parametry produktu z katalogu Allegro (Rolmar) oraz poprawki błędów parametrów z logów
    /// synchronizacji (Rolmar, Gąska).
    /// </summary>
    public class CatalogParametersOnlyTests
    {
        private static SearchProdustsResponse.Product Candidate(params (string Id, string Name, string? Value)[] parameters) => new()
        {
            Id = "p1",
            Category = new SearchProdustsResponse.Category { Id = "85198" },
            Parameters = parameters.Select(p => new SearchProdustsResponse.Parameter
            {
                Id = p.Id,
                Name = p.Name,
                Values = p.Value == null ? [] : [p.Value]
            }).ToList()
        };

        private static CategoryParameterItem Required(string id, string name, bool forProduct = true, bool describes = true, bool required = true) => new()
        {
            Id = id,
            Name = name,
            Required = required,
            RequiredForProduct = forProduct,
            Options = new Options { DescribesProduct = describes }
        };

        [Fact]
        public void Catalog_product_without_required_parameter_is_reported()
        {
            var candidate = Candidate(("1", "Numer katalogowy części", "LPMT01"), ("2", "Szerokość głowicy", null));

            var missing = AllegroProductService.MissingRequiredParameters(candidate,
                [Required("1", "Numer katalogowy części"), Required("2", "Szerokość głowicy")]);

            Assert.Equal(["Szerokość głowicy"], missing);
        }

        [Fact]
        public void Complete_catalog_product_has_nothing_missing()
        {
            var candidate = Candidate(("1", "Numer katalogowy części", "LPMT01"), ("2", "Szerokość głowicy", "120 cm"));

            Assert.Empty(AllegroProductService.MissingRequiredParameters(candidate,
                [Required("1", "Numer katalogowy części"), Required("2", "Szerokość głowicy")]));
        }

        [Fact]
        public void Required_offer_parameter_is_not_demanded_from_catalog_product()
        {
            // "Stan" uzupełniamy sami w sekcji oferty - produkt z katalogu nie musi go mieć.
            var offerParameter = Required("11323", "Stan", forProduct: false, describes: false);

            Assert.False(AllegroProductService.IsRequiredForCatalogProduct(offerParameter));
            Assert.Empty(AllegroProductService.MissingRequiredParameters(Candidate(), [offerParameter]));
        }

        [Theory]
        [InlineData("W-5036020600", "5036020600")]
        [InlineData("T-G-LPMT-01", null)]
        [InlineData("WMP128", null)]
        [InlineData("T-BHP-1009", null)]
        public void Rolmar_group_prefix_is_stripped_from_the_number(string code, string? expected)
        {
            var variants = AllegroProductService.RolmarNumberVariants(code).ToList();

            Assert.Equal(code, variants[0]);

            if (expected == null)
                Assert.Single(variants);
            else
                Assert.Equal(expected, variants[1]);
        }

        [Fact]
        public void Required_offer_parameter_is_the_only_one_computed_for_catalog_products()
        {
            Assert.True(AllegroParametersService.IsRequiredOfferParameter(new CategoryParameter { Required = true, DescribesProduct = false }));
            Assert.False(AllegroParametersService.IsRequiredOfferParameter(new CategoryParameter { Required = true, DescribesProduct = true }));
            Assert.False(AllegroParametersService.IsRequiredOfferParameter(new CategoryParameter { Required = false, RequiredForProduct = true }));
        }

        [Theory]
        [InlineData("ConstraintViolationException.MissingRequiredParameters", "Uzupełnij parametry obowiązkowe: Szerokość głowicy.", "", true)]
        [InlineData("ProductConstraintViolationException.DataIntegrity",
            "Niepoprawna wartość parametru EAN/ISBN/ISSN (225693) dla produktu w ofercie. Prawidłowa wartość parametru dla produktu to: \"5902287241372\".", "", true)]
        [InlineData("ProductConstraintViolationException.DataIntegrity", "Produkt został odłączony od oferty.", "", false)]
        [InlineData("DictionaryParameterValueNotFound", "", "An incorrect value `bez` has been entered for parameter `Marka`.", false)]
        public void Catalog_product_defects_are_recognised(string code, string userMessage, string message, bool expected)
        {
            Assert.Equal(expected, AllegroOfferService.IsCatalogProductDefect(code, userMessage, message));
        }

        [Fact]
        public void Dictionary_value_error_is_parsed()
        {
            var (value, parameter) = AllegroOfferService.ExtractDictionaryValueError("An incorrect value `Inna` has been entered for parameter `Marka`.");

            Assert.Equal("Inna", value);
            Assert.Equal("Marka", parameter);
        }

        [Fact]
        public void Missing_catalog_product_id_is_parsed()
        {
            Assert.Equal("59374511-c39b-40ec-82a5-72f616cd8eb1",
                AllegroOfferService.ExtractMissingProductId("Product with ID: 59374511-c39b-40ec-82a5-72f616cd8eb1 not found"));
        }

        [Fact]
        public void Required_brand_without_match_is_unbranded_not_ambiguous()
        {
            // Allegro odrzucało "Inna" w "Marka" (DictionaryParameterValueNotFound).
            var param = new CategoryParameter
            {
                Name = "Marka",
                Type = "dictionary",
                Required = true,
                AmbiguousValue = "Inna",
                Values = new[] { "Inna", "bez marki", "Rolmar" }.Select(v => new CategoryParameterValue { Value = v }).ToList()
            };

            Assert.Equal("bez marki", AllegroParametersService.MapProductToParameter(new RolmarProduct { Code = "GR.MSR/P/F-5" }, param));
        }

        [Fact]
        public void Ambiguous_value_outside_the_dictionary_is_not_sent()
        {
            var param = new CategoryParameter
            {
                Name = "Typ",
                Type = "dictionary",
                Required = true,
                AmbiguousValue = "Inna",
                Values = new[] { "Rozrzutnik", "Siewnik" }.Select(v => new CategoryParameterValue { Value = v }).ToList()
            };

            Assert.Null(ParameterFallbacks.ForRequired(param, new RolmarProduct { Code = "T-O-MISZ042" }));
        }

        [Theory]
        [InlineData("Marka", "bez marki", new[] { "bez marki" })]
        [InlineData("Marka", "John Deere, JAG", new[] { "John Deere", "JAG" })]
        [InlineData("Numery katalogowe zamienników", "AH1 AH2,AH3", new[] { "AH1", "AH2", "AH3" })]
        public void Multi_value_parameters_keep_brand_names_whole(string name, string value, string[] expected)
        {
            var values = value.Split(ParameterFallbacks.MultiValueSeparators(name), StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .ToArray();

            Assert.Equal(expected, values);
        }

        [Theory]
        [InlineData("Przedmiot, który chcesz wystawić znajduje się już na liście Twoich aktywnych ofert. Zgodnie z Regulaminem Allegro...", true)]
        [InlineData("Tytuł Twojej oferty lub jej dalszy opis może wprowadzać w błąd co do pochodzenia towaru", false)]
        public void Duplicate_active_offer_is_recognised(string userMessage, bool expected)
        {
            Assert.Equal(expected, AllegroOfferService.IsDuplicateActiveOffer(userMessage, string.Empty));
        }

        private static readonly CategoryParameterItem[] TargetCategory =
        [
            Required("11323", "Stan", forProduct: false, describes: false),
            Required("2", "Szerokość głowicy"),
            Required("3", "Kolor", forProduct: false, describes: false, required: false),
            Required("225693", "EAN (GTIN)", forProduct: true, describes: true)
        ];

        private static CategoryMove Move(bool linked, params Parameter[] retained) =>
            new(85198, [], TargetCategory, retained.ToList(), linked);

        private static ProductOfferRequest Patch(params (string Name, string Value)[] offerParameters) => new()
        {
            Parameters = offerParameters.Select(p => new Parameter { Name = p.Name, Values = [p.Value] }).ToList()
        };

        [Fact]
        public void Move_completeness_is_checked_on_what_the_patch_sends()
        {
            // Produkt z katalogu: parametry produktu daje katalog, wymagany jest tylko obowiązkowy parametr oferty.
            Assert.Equal(["Stan"], OfferCategoryAligner.MissingIn(Move(linked: true), Patch()));
            Assert.Empty(OfferCategoryAligner.MissingIn(Move(linked: true), Patch(("Stan", "Nowy"))));

            // Propozycja produktu: potrzebne także parametry produktu - EAN w bazie nie wystarczy, musi pójść w patchu.
            Assert.Equal(["Szerokość głowicy", "EAN (GTIN)"], OfferCategoryAligner.MissingIn(Move(linked: false), Patch(("Stan", "Nowy"))));
        }

        [Fact]
        public void Move_keeps_only_offer_parameters_the_target_category_knows()
        {
            var current = new List<AllegroOfferDetails.Parameter>
            {
                new() { Id = "11323", Name = "Stan", ValuesIds = ["11323_1"] },
                new() { Id = "247835", Name = "Producent części", Values = ["SAPALA"] },   // brak w kategorii docelowej
                new() { Id = "225693", Name = "EAN (GTIN)", Values = ["5902287241372"] },  // parametr produktu
                new() { Id = "3", Name = "Kolor", Values = ["czarny"] }
            };

            var retained = OfferCategoryAligner.Retained(current, TargetCategory);

            Assert.Equal(["11323", "3"], retained.Select(p => p.Id ?? string.Empty).ToArray());
            Assert.Equal(["11323_1"], retained[0].ValuesIds);
            Assert.Empty(retained[0].Values);
        }

        [Fact]
        public void Move_sends_a_full_parameter_list_with_our_values_first()
        {
            var move = Move(linked: true,
                new Parameter { Id = "11323", Name = "Stan", ValuesIds = ["11323_2"] },
                new Parameter { Id = "3", Name = "Kolor", Values = ["czarny"] });

            var patch = Patch(("Stan", "Nowy"));
            OfferCategoryAligner.ApplyTo(move, patch);

            Assert.Equal("85198", patch.Category?.Id);
            Assert.Equal(["Stan", "Kolor"], patch.Parameters!.Select(p => p.Name ?? string.Empty).ToArray());
            Assert.Equal(["Nowy"], patch.Parameters![0].Values);
        }

        [Fact]
        public void Missing_parameter_names_are_read_from_the_message()
        {
            Assert.Equal(["EAN (GTIN)", "Marka", "Gatunek kawy"],
                AllegroOfferService.ExtractMissingParameterNames("Uzupełnij parametry obowiązkowe: EAN (GTIN), Marka, Gatunek kawy."));
            Assert.Empty(AllegroOfferService.ExtractMissingParameterNames("Opis zawiera zdjęcia spoza galerii"));
        }

        [Fact]
        public void Replacement_must_fill_the_parameters_allegro_asked_for()
        {
            var candidate = Candidate(("1", "Marka maszyny", "Ursus"), ("2", "Numer katalogowy części", null));

            Assert.Equal(["Numer katalogowy części"],
                AllegroProductService.MissingNamedParameters(candidate, ["Marka maszyny", "Numer katalogowy części"]).ToList());
        }

        [Theory]
        [InlineData("{\"errors\":[{\"code\":\"GallerySizeException\",\"message\":\"You can add up to 16 images.\"}]}", 16)]
        [InlineData("{\"errors\":[{\"code\":\"ConstraintViolationException.GallerySize\",\"userMessage\":\"Musisz dodać przynajmniej 1 zdjęcie.\"}]}", null)]
        [InlineData("{\"errors\":[{\"code\":\"OfferValidation\",\"message\":\"x\"}]}", null)]
        public void Gallery_over_the_limit_is_recognised(string body, int? expected)
        {
            Assert.Equal(expected, AllegroOfferService.GalleryTooLargeLimit(body));
        }

        [Theory]
        [InlineData(16, 4, 16, 12)]   // katalog dolicza 4 zdjęcia
        [InlineData(16, 0, 16, 15)]   // nie wiadomo, co dolicza - zdejmujemy jedno
        [InlineData(10, 20, 16, 1)]   // katalog sam przekracza limit - zostaje jedno zdjęcie
        public void Own_gallery_is_reduced_to_fit(int own, int catalog, int limit, int expected)
        {
            Assert.Equal(expected, AllegroOfferService.ReducedGallerySize(own, catalog, limit));
        }

        [Theory]
        [InlineData("5902287241372", true)]
        [InlineData("2014802786572", false)]
        [InlineData("2014802712267", false)]
        [InlineData("12345670", true)]
        public void In_store_rcn_eans_are_not_sent(string ean, bool expected)
        {
            // "EAN (GTIN), który zaczyna się od 201 to Numer Ograniczonej Dystrybucji - RCN" (Gąska, 647393.01).
            Assert.Equal(expected, ParameterFallbacks.IsPublicGtin(ean));
        }

        [Theory]
        [InlineData("5902287241372", true)]
        [InlineData("5902287241373", false)]
        [InlineData("2014802712267", true)]
        [InlineData("590228724137", false)]
        [InlineData("ABC", false)]
        [InlineData(null, false)]
        public void Gtin_checksum_is_validated(string? value, bool expected)
        {
            Assert.Equal(expected, ParameterFallbacks.IsValidGtin(value));
        }
    }
}
