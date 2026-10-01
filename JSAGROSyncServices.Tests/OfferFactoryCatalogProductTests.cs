using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Options;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Rolmar i Inter Cars wystawiają wyłącznie na istniejącym produkcie z katalogu Allegro.
    /// Dołożenie nazwy, zdjęć albo parametrów obok id Allegro czyta jako propozycję zmiany
    /// produktu i wymaga wtedy kompletu parametrów produktowych, których dane dostawców
    /// nie zawierają - oferta kończyła się błędem "Uzupełnij parametry obowiązkowe".
    /// </summary>
    public class OfferFactoryCatalogProductTests
    {
        private static RolmarProduct Product(string? allegroId) => new()
        {
            Id = 1,
            Code = "TEST-1",
            Name = "Zaczep transportowy ciągnika rolniczego",
            Ean = "5901234123457",
            Weight = 5,
            Package = 1,
            Unit = "szt",
            PriceNet = 100m,
            PriceGross = 123m,
            InStock = 10,
            DefaultAllegroCategory = 319123,
            AllegroId = allegroId,
            AllegroName = "Zaczep transportowy",
            Parameters =
            [
                new ProductParameter { Name = "Stan", Value = "Nowy", IsForProduct = false },
                new ProductParameter { Name = "Producent części", Value = "SAPALA", IsForProduct = true }
            ],
            AllegroImages = [new AllegroImages { Url = "https://example.test/a.jpg" }]
        };

        /// <summary>Minimalna konfiguracja cen - bez przedziału marży fabryka Rolmaru nie policzy ceny.</summary>
        private static PriceSettings Prices() => new()
        {
            AllegroMarginBetween5and1000PLNPercent = 10m,
            MarginRanges = [new MarginRange { Min = 0m, Max = 100000m, Margin = 20m }]
        };

        private static RolmarOfferFactory Rolmar() => new(
            Options.Create(new AppSettings { Deliveries = TestProducts.Deliveries() }),
            Options.Create(new AllegroSettings()),
            Options.Create(Prices()));

        private static InterCarsOfferFactory InterCars() => new(
            Options.Create(new AppSettings { Deliveries = TestProducts.Deliveries() }),
            Options.Create(new AllegroSettings()),
            Options.Create(Prices()));

        [Fact]
        public void Rolmar_requires_a_catalog_product()
        {
            Assert.True(Rolmar().RequiresCatalogProduct);
        }

        [Fact]
        public void InterCars_requires_a_catalog_product()
        {
            Assert.True(InterCars().RequiresCatalogProduct);
        }

        [Fact]
        public void Rolmar_sends_only_the_catalog_product_id()
        {
            var offer = Rolmar().BuildOffer(Product("4cc75215-e316-4f42-ab05-ee41eca23708"));

            var product = Assert.Single(offer.ProductSet).ProductObject;

            Assert.Equal("4cc75215-e316-4f42-ab05-ee41eca23708", product!.Id);
            Assert.Null(product.Name);
            Assert.Null(product.Category);
            Assert.Null(product.Parameters);
            Assert.Null(product.Images);
        }

        [Fact]
        public void InterCars_sends_only_the_catalog_product_id()
        {
            var offer = InterCars().BuildOffer(Product("4cc75215-e316-4f42-ab05-ee41eca23708"));

            var product = Assert.Single(offer.ProductSet).ProductObject;

            Assert.Equal("4cc75215-e316-4f42-ab05-ee41eca23708", product!.Id);
            Assert.Null(product.Name);
            Assert.Null(product.Category);
            Assert.Null(product.Parameters);
            Assert.Null(product.Images);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_product_proposal_is_built_without_a_catalog_id(string? allegroId)
        {
            // Produkt bez dopasowania odsiewa RequiresCatalogProduct, zanim dojdzie do fabryki.
            // Gdyby jednak doszedł, nie wolno wyslac propozycji nowego produktu.
            foreach (var offer in new[] { Rolmar().BuildOffer(Product(allegroId)), InterCars().BuildOffer(Product(allegroId)) })
            {
                var product = Assert.Single(offer.ProductSet).ProductObject;

                Assert.Null(product!.Name);
                Assert.Null(product.Parameters);
            }
        }

        [Fact]
        public void Offer_parameters_are_still_sent_and_product_only_ones_are_not()
        {
            // Parametry oferty bierze Allegro od nas; parametry opisujące produkt ma własne.
            var offer = InterCars().BuildOffer(Product("4cc75215-e316-4f42-ab05-ee41eca23708"));

            Assert.NotNull(offer.Parameters);
            Assert.Contains(offer.Parameters!, p => p.Name == "Stan");
            Assert.DoesNotContain(offer.Parameters!, p => p.Name == "Producent części");
        }
    }
}
