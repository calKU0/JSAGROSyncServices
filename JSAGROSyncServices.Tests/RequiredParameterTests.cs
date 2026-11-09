using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Services.Allegro;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Parametry obowiązkowe Allegro: dopasowanie do słownika i wartości zastępcze.</summary>
    public class RequiredParameterTests
    {
        private static CategoryParameter Dictionary(string name, bool required, string? ambiguous, params string[] values) => new()
        {
            Name = name,
            Type = "dictionary",
            Required = required,
            AmbiguousValue = ambiguous,
            Values = values.Select(v => new CategoryParameterValue { Value = v }).ToList()
        };

        private static RolmarProduct Product(string? supplier = null, params string[] machineBrands) => new()
        {
            Code = "JAG98-0420",
            SupplierName = supplier,
            Applications = machineBrands.Select((b, i) => new ProductApplication { Id = i + 1, Name = b, ParentID = 0 }).ToList()
        };

        [Fact]
        public void Machine_brand_from_applications_is_matched_without_diacritics()
        {
            var param = Dictionary("Marka maszyny", true, "Inna", "Ursus", "Władimirec", "Inna");

            Assert.Equal("Władimirec", AllegroParametersService.MapProductToParameter(Product(null, "WLADIMIREC"), param));
        }

        [Fact]
        public void Unknown_machine_brand_gets_the_ambiguous_value_of_the_category()
        {
            var param = Dictionary("Marka maszyny", true, "inna marka", "Ursus", "inna marka");

            Assert.Equal("inna marka", AllegroParametersService.MapProductToParameter(Product("VOIMA", "Lamborghini"), param));
        }

        [Fact]
        public void Without_ambiguous_value_a_generic_dictionary_value_is_used()
        {
            var param = Dictionary("Producent części", true, null, "Bosch", "nieznany producent");

            Assert.Equal("nieznany producent", AllegroParametersService.MapProductToParameter(Product("Rolmar"), param));
        }

        [Fact]
        public void Supplier_brand_is_used_when_dictionary_knows_it()
        {
            var param = Dictionary("Producent części", true, "nieznany producent", "Rolmar", "nieznany producent");

            Assert.Equal("Rolmar", AllegroParametersService.MapProductToParameter(Product("ROLMAR"), param));
        }

        [Fact]
        public void Product_brand_is_the_supplier_brand_or_no_brand()
        {
            var param = Dictionary("Marka", true, null, "Ursus", "Rolmar", "bez marki");

            // Marka maszyny z zastosowań nie jest marką produktu.
            Assert.Equal("Rolmar", AllegroParametersService.MapProductToParameter(Product("ROLMAR", "Ursus"), param));
            Assert.Equal("bez marki", AllegroParametersService.MapProductToParameter(Product("Voima", "Ursus"), param));
        }

        [Fact]
        public void Optional_parameter_outside_the_dictionary_is_left_empty()
        {
            var param = Dictionary("Marka maszyny", false, "Inna", "Ursus", "Inna");

            Assert.Null(AllegroParametersService.MapProductToParameter(Product(null, "Lamborghini"), param));
        }

        [Fact]
        public void Required_text_parameters_use_the_product_data()
        {
            var catalogNumber = new CategoryParameter { Name = "Numer katalogowy części", Type = "string", Required = true };
            var manufacturer = new CategoryParameter { Name = "Producent części", Type = "string", Required = true };

            Assert.Equal("JAG98-0420", AllegroParametersService.MapProductToParameter(Product(), catalogNumber));
            Assert.Equal("nieznany producent", AllegroParametersService.MapProductToParameter(Product(), manufacturer));
            Assert.Equal("JAG", AllegroParametersService.MapProductToParameter(Product("JAG"), manufacturer));
        }

        [Theory]
        [InlineData("Liczba sztuk w opakowaniu")]
        [InlineData("Liczba sztuk w zestawie")]
        [InlineData("Liczba sztuk w ofercie")]
        public void Piece_count_is_one(string name)
        {
            var param = new CategoryParameter { Name = name, Type = "integer", Required = true };

            Assert.Equal("1", AllegroParametersService.MapProductToParameter(Product(), param));
        }

        [Fact]
        public void Unknown_required_number_is_not_invented()
        {
            var param = new CategoryParameter { Name = "Pojemność", Type = "float", Required = true };

            Assert.Null(AllegroParametersService.MapProductToParameter(Product(), param));
        }

        [Fact]
        public void Offer_gets_parameters_of_its_own_category()
        {
            List<ProductParameter> parameters =
            [
                new() { Name = "Marka maszyny", Value = "Ursus", CategoryId = 319120 },
                new() { Name = "Marka maszyny", Value = "Inna", CategoryId = 252436 },
                new() { Name = "Producent części", Value = "JAG", CategoryId = 252436 }
            ];

            var forOffer = ParameterFallbacks.ForCategory(parameters, offerCategoryId: 252436, defaultCategoryId: 319120);
            Assert.Equal(2, forOffer.Count);
            Assert.All(forOffer, p => Assert.Equal(252436, p.CategoryId));

            // Kategoria oferty jeszcze bez parametrów - zestaw z kategorii domyślnej.
            var fallback = ParameterFallbacks.ForCategory(parameters, offerCategoryId: 1, defaultCategoryId: 319120);
            Assert.Equal("Ursus", Assert.Single(fallback).Value);

            // Żadna z dwóch kategorii nie ma zestawu - parametrów z niezwiązanej kategorii nie wysyłamy.
            Assert.Empty(ParameterFallbacks.ForCategory(parameters, offerCategoryId: 1, defaultCategoryId: 2));
        }

        [Fact]
        public void Legacy_parameters_without_category_are_still_used()
        {
            List<ProductParameter> parameters =
            [
                new() { Name = "Stan", Value = "Nowy" },
                new() { Name = "Stan", Value = "Używany" },
                new() { Name = "Marka", Value = "Ursus", CategoryId = 252436 }
            ];

            Assert.Equal("Nowy", Assert.Single(ParameterFallbacks.ForCategory(parameters, 1, 2)).Value);
        }

        [Fact]
        public void Catalog_product_gets_product_parameters_from_other_categories()
        {
            // Oferta w 259668 ("Marka"), produkt z katalogu w 319159 ("Marka maszyny").
            List<ProductParameter> parameters =
            [
                new() { Name = "Marka", Value = "Groz", CategoryId = 259668, IsForProduct = true },
                new() { Name = "Marka maszyny", Value = "Inna", CategoryId = 319159, IsForProduct = true },
                new() { Name = "Marka", Value = "Inna", CategoryId = 319159, IsForProduct = true },
                new() { Name = "Stan", Value = "Nowy", CategoryId = 319159, IsForProduct = false }
            ];

            var plain = ParameterFallbacks.ForCategory(parameters, 259668, 259668);
            Assert.Equal("Marka", Assert.Single(plain).Name);

            var withCatalog = ParameterFallbacks.ForCategory(parameters, 259668, 259668, withOtherProductParameters: true);
            Assert.Equal(["Marka", "Marka maszyny"], withCatalog.Select(p => p.Name));
            Assert.Equal("Groz", withCatalog[0].Value);
        }
    }
}
