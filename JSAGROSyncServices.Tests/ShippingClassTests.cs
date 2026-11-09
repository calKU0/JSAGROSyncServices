using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Mappers;
using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Settings;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Klasa wysyłki dostawcy ("cubature" Rolmara) jako zapasowe źródło gabarytu.</summary>
    public class ShippingClassTests
    {
        [Theory]
        [InlineData("INPOST_A", 8)]
        [InlineData("inpost_b", 19)]
        [InlineData(" INPOST_C ", 38)]
        public void Locker_classes_have_parcel_locker_dimensions(string shippingClass, decimal smallestSide)
        {
            var sides = ShippingClasses.ParcelSidesCm(shippingClass)!;

            Assert.Equal(64m, sides[0]);
            Assert.Equal(smallestSide, sides[2]);
        }

        [Theory]
        [InlineData("półpaleta", true)]
        [InlineData("paleta", true)]
        [InlineData("dłużyca", true)]
        [InlineData("paczka", false)]
        [InlineData("INPOST_A", false)]
        [InlineData(null, false)]
        public void Pallet_and_long_goods_are_freight(string? shippingClass, bool expected) =>
            Assert.Equal(expected, ShippingClasses.IsFreight(shippingClass));

        [Fact]
        public void Allegro_price_list_uses_shipping_class_when_specification_has_no_dimensions()
        {
            List<DeliverySettings> deliveries =
            [
                new() { Width = 10, Length = 10, Height = 10, Weight = 20m, DeliveryName = "Koperta" },
                new() { Width = 41, Length = 64, Height = 38, Weight = 20m, DeliveryName = "Mały" }
            ];

            var product = new RolmarProduct { Weight = 1f, Specifications = new() };

            // Bez wymiarów decyduje sama waga - pierwszy cennik.
            Assert.Equal("Koperta", DeliveryMatcher.Match(product, deliveries));

            // Klasa paczkomatu A to 64x38x8 cm - do koperty się nie zmieści.
            product.ShippingClass = "INPOST_A";
            Assert.Equal("Mały", DeliveryMatcher.Match(product, deliveries));
        }

        private static InPostOfferBuilder Builder() => new(
            new Allegro.JSAGRO.InPost.ProductsService.Settings.AppSettings
            {
                MinProductStock = 1,
                DeliveryPriceLists =
                [
                    new DeliveryPriceListSettings { Name = "Mały", IntegrationCompany = IntegrationCompany.Rolmar, DefaultBrand = "Rolmar", FallbackLengthCm = 64, FallbackWidthCm = 41, FallbackHeightCm = 38 }
                ]
            },
            new InPostPriceSettings { MarginRanges = [new MarginRange { Min = 0, Max = 999999, Margin = 10 }] },
            new InPostOfferSettings { BrandRules = [new BrandRule { Keyword = "Waryński", Brand = "Waryński" }] },
            [], []);

        private static InPostSourceOffer Offer(string? shippingClass, params (string Name, string Value, string Unit)[] specifications) => new()
        {
            Id = "1",
            Name = "Część",
            DeliveryName = "Mały",
            Images = "[\"https://a.allegroimg.com/original/1\"]",
            Product = new RolmarProduct
            {
                Code = "X-1",
                Name = "Część",
                InStock = 5,
                PriceGross = 10m,
                Weight = 0.5f,
                ShippingClass = shippingClass,
                Specifications = specifications.Select(s => new ProductSpecification { Name = s.Name, Value = s.Value, UnitName = s.Unit }).ToList()
            }
        };

        [Fact]
        public void InPost_uses_locker_size_before_price_list_parcel()
        {
            var dimension = Builder().Build(Offer("INPOST_B"), "cat", []).Proposal!.Product.Dimension!;

            Assert.Equal(DimensionSource.ShippingClass, dimension.Source);
            Assert.Equal(new int?[] { 640, 380, 190 }, new[] { dimension.Length, dimension.Width, dimension.Height });
        }

        [Fact]
        public void InPost_prefers_full_product_dimensions_over_shipping_class()
        {
            var dimension = Builder().Build(Offer("INPOST_C", ("Długość", "20", "cm"), ("Szerokość", "10", "cm"), ("Wysokość", "5", "cm")), "cat", []).Proposal!.Product.Dimension!;

            Assert.Equal(DimensionSource.Product, dimension.Source);
            Assert.Equal(200, dimension.Length);
        }

        [Fact]
        public void InPost_falls_back_to_price_list_when_known_side_does_not_fit_the_locker()
        {
            // Znany bok 90 cm jest dłuższy niż skrytka paczkomatu - klasa A jest sprzeczna z wymiarem.
            var dimension = Builder().Build(Offer("INPOST_A", ("Długość", "90", "cm")), "cat", []).Proposal!.Product.Dimension!;

            Assert.Equal(DimensionSource.PriceList, dimension.Source);
            // Paczka cennika 64x41x38, ale znany bok 90 cm nie może zostać zaniżony.
            Assert.Equal(new int?[] { 900, 410, 380 }, new[] { dimension.Length, dimension.Width, dimension.Height });
        }

        [Fact]
        public void Content_hash_ignores_price_and_stock_but_tracks_content()
        {
            var first = Builder().Build(Offer("INPOST_A"), "cat", []);
            var cheaper = Offer("INPOST_A");
            cheaper.Product.PriceGross = 5m;
            cheaper.Product.InStock = 99;
            var changedDescription = Offer("INPOST_A");
            changedDescription.Descriptions.Add(new AllegroOfferDescription { Type = "TEXT", Content = "<p>Nowy opis</p>" });

            Assert.Equal(first.ContentHash, Builder().Build(cheaper, "cat", []).ContentHash);
            Assert.NotEqual(first.ContentHash, Builder().Build(changedDescription, "cat", []).ContentHash);
        }

        [Theory]
        [InlineData(5, 20, 10, false)]      // mało ofert - zawsze zamykamy
        [InlineData(100, 10000, 10, false)] // 1% - zwykła rotacja
        [InlineData(9000, 10000, 10, true)] // stan 0 u całego dostawcy - nie zamykamy
        [InlineData(9000, 10000, 0, false)] // bezpiecznik wyłączony
        public void Mass_close_guard(int toClose, int open, decimal maxPercent, bool blocked) =>
            Assert.Equal(blocked, Allegro.JSAGRO.InPost.ProductsService.Services.InPostService.IsMassClose(toClose, open, maxPercent));

        [Fact]
        public void InPost_skips_pallet_goods()
        {
            Assert.Contains("paletowy", Builder().Build(Offer("półpaleta"), "cat", []).SkipReason);
        }

        [Fact]
        public void Supplier_brand_wins_and_is_normalised_by_brand_rules()
        {
            var builder = Builder();
            var offer = Offer(null);

            offer.Product.SupplierName = "WARYNSKI";
            Assert.Equal("Waryński", builder.ResolveBrand(offer));

            offer.Product.SupplierName = "VOIMA";
            Assert.Equal("VOIMA", builder.ResolveBrand(offer));
        }
    }
}
