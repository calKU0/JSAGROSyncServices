using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Mappers;
using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Settings;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    public class InPostOfferBuilderTests
    {
        private const string TractorCategory = "baae6bf8-5043-5568-84c4-e0b3bd2abfd7";

        private static InPostPriceSettings Prices(decimal shipping = 8.99m, decimal commission = 0m) => new()
        {
            MarginRanges =
            [
                new MarginRange { Min = 0, Max = 50, Margin = 15 },
                new MarginRange { Min = 50.01m, Max = 99.99m, Margin = 15 },
                new MarginRange { Min = 100, Max = 999999, Margin = 5 }
            ],
            ShippingCostGross = shipping,
            CommissionPercent = commission,
            BulkyDeliveryPriceNet = 15,
            CustomDeliveryPriceNet = 244,
            OversizeSurcharge = 22,
            OversizeThresholdCm = 150,
            VatRate = 23
        };

        private static InPostOfferSettings OfferSettings() => new()
        {
            BrandRules =
            [
                new BrandRule { Keyword = "Waryński", Brand = "Waryński" },
                new BrandRule { Keyword = "URSUS", Brand = "URSUS" }
            ],
            ReturnPolicy = "Zwrot 14 dni",
            ComplaintPolicy = "Reklamacja 1 rok"
        };

        private static InPostOfferBuilder Builder(InPostPriceSettings? prices = null) => new(
            new Allegro.JSAGRO.InPost.ProductsService.Settings.AppSettings
            {
                MinProductStock = 2,
                DeliveryPriceLists =
                [
                    new DeliveryPriceListSettings { Name = "Mały Asortyment Rolmar", IntegrationCompany = IntegrationCompany.Rolmar, DefaultBrand = "Rolmar" },
                    new DeliveryPriceListSettings { Name = "JAG API", IntegrationCompany = IntegrationCompany.Gaska, DefaultBrand = "JAG" }
                ]
            },
            prices ?? Prices(),
            OfferSettings(),
            [new AllegroResponsibleProducer { AllegroId = "prod", Name = "Rolmar", TradeName = "Rolmar Trade sp. z o.o.", Email = "sekretariat@rolmar.pl", CountryCode = "PL", Street = "Kobiałka", PostalCode = "09-411", City = "Płock" }],
            [new AllegroResponsiblePerson { AllegroId = "person", Name = "JS AGRO", Email = "jsagro@wp.pl", CountryCode = "PL", Street = "Piłsudskiego 41", PostalCode = "17-100", City = "Bielsk Podlaski" }]);

        private static InPostSourceOffer Offer(params (string Name, string Value, string? Unit)[] specifications) => new()
        {
            Id = "123",
            DeliveryName = "Mały Asortyment Rolmar",
            Name = "Elektrozawór 3/2 G1/2\" 12V Waryński",
            CategoryId = 252216,
            Price = 358.40m,
            Images = "[\"https://a.allegroimg.com/original/1\",\"https://a.allegroimg.com/original/2\",\"https://a.allegroimg.com/original/1\"]",
            HandlingTime = "PT48H",
            ResponsibleProducer = "prod",
            ResponsiblePerson = "person",
            Descriptions =
            [
                new AllegroOfferDescription { SectionId = 1, Type = "IMAGE", Content = "https://a.allegroimg.com/original/3" },
                new AllegroOfferDescription { SectionId = 2, Type = "TEXT", Content = "<p><b>Elektrozawór</b></p>" }
            ],
            OfferParameters = [new NamedValues { Name = "Stan", Values = ["Nowy"] }],
            Product = new RolmarProduct
            {
                Code = "W-E12DVS3/2-80L",
                Name = "Elektrozawór 3/2 Waryński",
                Ean = "5902287148831",
                InStock = 24,
                Unit = "szt",
                PriceGross = 295.19m,
                Weight = 4.4f,
                Substitutes = "ABC-1, ABC-2",
                Parameters = [new ProductParameter { Name = "Producent części", Value = "nieznany producent" }],
                Specifications = specifications.Select(s => new ProductSpecification { Name = s.Name, Value = s.Value, UnitName = s.Unit! }).ToList()
            }
        };

        private static List<AttributeDefinition> PartAttributes() =>
        [
            new() { Id = "a1", Name = "Numer katalogowy części", Type = "TEXT_VALUE", ExpectedValue = "NULL_OR_ONE", Lang = "pl_PL" },
            new() { Id = "a2", Name = "Numery katalogowe zamienników", Type = "TEXT_VALUE", ExpectedValue = "ANY", Lang = "pl_PL" },
            new() { Id = "a3", Name = "Producent części", Type = "TEXT_VALUE", ExpectedValue = "NULL_OR_ONE", Lang = "pl_PL" },
            new() { Id = "a4", Name = "Jakość części (zgodnie z GVO)", Type = "TEXT_VALUE", ExpectedValue = "NULL_OR_ONE", Lang = "pl_PL" }
        ];

        [Fact]
        public void Price_is_built_from_purchase_price_not_copied_from_allegro()
        {
            var price = InPostPricing.Calculate(new RolmarProduct { PriceGross = 295.19m }, Prices());

            // 295.19 * 1.05 = 309.95 + 8.99 wysyłki = 318.94 (prowizja 0%).
            Assert.Equal(5m, price.MarginPercent);
            Assert.Equal(318.94m, price.Final);
        }

        [Fact]
        public void Commission_is_charged_on_the_whole_price_including_shipping()
        {
            var price = InPostPricing.Calculate(new RolmarProduct { PriceGross = 100m }, Prices(shipping: 10m, commission: 10m));

            // (100 * 1.05 + 10) * 1.10 = 126.50
            Assert.Equal(126.50m, price.Final);
        }

        [Theory]
        [InlineData(1, 18.45)]   // gabaryt: 15 netto * 1.23
        [InlineData(2, 300.12)]  // niestandardowy: 244 netto * 1.23
        [InlineData(0, 0)]
        public void Bulky_and_custom_delivery_surcharges_are_added_gross(int deliveryType, decimal expectedSurcharge)
        {
            var price = InPostPricing.Calculate(new RolmarProduct { PriceGross = 40m, DeliveryType = deliveryType }, Prices(shipping: 0m));

            Assert.Equal(expectedSurcharge, price.DeliveryTypeSurcharge);
            Assert.Equal(Math.Round(40m * 1.15m + expectedSurcharge, 2), price.Final);
        }

        [Fact]
        public void Accepted_price_is_used_while_a_suspicious_price_drop_is_pending()
        {
            var product = new RolmarProduct { PriceGross = 10m, AcceptedPriceGross = 100m, PriceDropDetectedAt = DateTime.UtcNow };

            Assert.Equal(100m, InPostPricing.PurchasePriceGross(product));
        }

        [Fact]
        public void Builds_full_offer()
        {
            var result = Builder().Build(Offer(), TractorCategory, PartAttributes());

            Assert.False(result.IsSkipped, result.SkipReason);
            var p = result.Proposal!;

            Assert.Equal("W-E12DVS3/2-80L", p.ExternalId);
            Assert.Equal("W-E12DVS3/2-80L", p.Product.Sku);
            Assert.Equal("W-E12DVS3/2-80L", p.Product.ManufacturerProductNumber);
            Assert.Equal("5902287148831", p.Product.Ean);
            Assert.Equal("Waryński", p.Product.Brand);
            Assert.Equal(TractorCategory, p.Product.CategoryId);
            Assert.Equal("<p><b>Elektrozawór</b></p>", p.Product.Description);
            Assert.Equal(24, p.Stock.Quantity);
            Assert.Equal("UNIT", p.Stock.Unit);
            Assert.Equal("23%", p.Price.TaxRateInfo);
            Assert.Equal(2, p.ShippingTime!.DaysToShip);
            Assert.Equal(4400, p.Product.Dimension!.Weight);
            Assert.Equal("Zwrot 14 dni", p.PostSale!.ReturnPolicy!.Description);

            // Galeria bez duplikatów, zdjęcie z opisu na końcu.
            Assert.Equal(new[] { "https://a.allegroimg.com/original/1", "https://a.allegroimg.com/original/2", "https://a.allegroimg.com/original/3" },
                p.Images!.Select(i => i.FileUrl));
            Assert.Equal(new[] { 1, 2, 3 }, p.Images!.Select(i => i.Priority));

            Assert.Equal("Rolmar Trade sp. z o.o.", p.Gpsr!.Manufacturer!.Name);
            Assert.Equal("Kobiałka, 09-411 Płock", p.Gpsr.Manufacturer.UnstructuredAddress);
            Assert.Equal("JS AGRO", p.Gpsr.Manufacturer.ResponsiblePersonDetails!.Name);
        }

        [Fact]
        public void Attributes_are_filled_by_name_and_meaningless_values_are_replaced()
        {
            var p = Builder().Build(Offer(), TractorCategory, PartAttributes()).Proposal!;
            var byId = p.Product.Attributes!.ToDictionary(a => a.Id, a => a.Values);

            Assert.Equal(new[] { "W-E12DVS3/2-80L" }, byId["a1"]);
            Assert.Equal(new[] { "ABC-1", "ABC-2" }, byId["a2"]);
            // "nieznany producent" z parametru zastępuje marka.
            Assert.Equal(new[] { "Waryński" }, byId["a3"]);
            // Dostawca nie podał jakości - zamiennik porównywalny z oryginałem.
            Assert.Equal(new[] { "P - zamiennik o jakości porównywalnej do oryginału" }, byId["a4"]);
        }

        [Fact]
        public void Part_quality_from_supplier_wins_over_default()
        {
            var offer = Offer();
            offer.OfferParameters.Add(new NamedValues { Name = "Jakość części (zgodnie z GVO)", Values = ["O - oryginał"] });

            var byId = Builder().Build(offer, TractorCategory, PartAttributes()).Proposal!.Product.Attributes!.ToDictionary(a => a.Id, a => a.Values);

            Assert.Equal(new[] { "O - oryginał" }, byId["a4"]);
        }

        [Fact]
        public void Dimensions_are_sent_in_millimetres_longest_side_as_length()
        {
            var p = Builder().Build(Offer(("Długość", "40", "cm"), ("Szerokość", "12", "cm"), ("Wysokość", "8", "cm")), TractorCategory, PartAttributes()).Proposal!;

            Assert.Equal(400, p.Product.Dimension!.Length);
            Assert.Equal(120, p.Product.Dimension.Width);
            Assert.Equal(80, p.Product.Dimension.Height);
        }

        [Fact]
        public void Price_list_parcel_is_used_when_product_has_no_dimensions()
        {
            var builder = new InPostOfferBuilder(
                new Allegro.JSAGRO.InPost.ProductsService.Settings.AppSettings
                {
                    MinProductStock = 2,
                    DeliveryPriceLists =
                    [
                        new DeliveryPriceListSettings { Name = "Mały Asortyment Rolmar", DefaultBrand = "Rolmar", FallbackLengthCm = 41, FallbackWidthCm = 64, FallbackHeightCm = 38 }
                    ]
                },
                Prices(), OfferSettings(), [], []);

            var withoutDimensions = builder.Build(Offer(), TractorCategory, PartAttributes()).Proposal!.Product.Dimension!;

            Assert.Equal(640, withoutDimensions.Length);
            Assert.Equal(410, withoutDimensions.Width);
            Assert.Equal(380, withoutDimensions.Height);
            Assert.Equal(4400, withoutDimensions.Weight);

            // Znane wymiary produktu mają pierwszeństwo przed paczką z cennika.
            var withDimensions = builder.Build(Offer(("Długość", "40", "cm"), ("Szerokość", "12", "cm"), ("Wysokość", "8", "cm")), TractorCategory, PartAttributes()).Proposal!.Product.Dimension!;

            Assert.Equal(400, withDimensions.Length);

            // Jeden znany bok ponad limit - paczka z cennika go nie "ukrywa".
            Assert.Contains("Za duży", builder.Build(Offer(("Długość", "150", "cm")), TractorCategory, PartAttributes()).SkipReason);
        }

        [Fact]
        public void Product_longer_than_standard_parcel_is_skipped()
        {
            var result = Builder().Build(Offer(("Długość", "130", "cm")), TractorCategory, PartAttributes());

            Assert.True(result.IsSkipped);
            Assert.Contains("Za duży", result.SkipReason);
        }

        [Fact]
        public void Product_heavier_than_courier_limit_is_skipped()
        {
            var offer = Offer();
            offer.Product.Weight = 31.5f;

            Assert.Contains("Za ciężki", Builder().Build(offer, TractorCategory, PartAttributes()).SkipReason);
        }

        [Fact]
        public void Product_below_minimum_stock_is_skipped()
        {
            var offer = Offer();
            offer.Product.InStock = 1;

            Assert.True(Builder().Build(offer, TractorCategory, PartAttributes()).IsSkipped);
        }

        [Fact]
        public void Brand_falls_back_to_supplier_category_then_default()
        {
            var builder = Builder();
            var offer = Offer();
            offer.Name = offer.Product.Name = "Wąż hydrauliczny DN10";

            Assert.Equal("Rolmar", builder.ResolveBrand(offer));

            offer.DeliveryName = "JAG API";
            Assert.Equal("JAG", builder.ResolveBrand(offer));

            offer.SupplierCategoryPaths.Add("CZĘŚCI ORYGINALNE URSUS>C-330");
            Assert.Equal("URSUS", builder.ResolveBrand(offer));
        }

        [Theory]
        [InlineData("5902287148831", "5902287148831")]
        [InlineData(" 590-2287148831 ", "5902287148831")]
        [InlineData("123", null)]
        [InlineData(null, null)]
        public void Only_valid_ean_is_sent(string? ean, string? expected) =>
            Assert.Equal(expected, InPostOfferBuilder.NormalizeEan(ean));

        private static List<InPostCategory> InPostTree() =>
        [
            new()
            {
                Id = "moto", Name = "Motoryzacja", Children =
                [
                    new()
                    {
                        Id = "maszyny", Name = "Części do maszyn i innych pojazdów", Children =
                        [
                            new()
                            {
                                Id = "rolnicze", Name = "Części do maszyn rolniczych", Children =
                                [
                                    new() { Id = TractorCategory, Name = "Do traktorów", Leaf = true }
                                ]
                            }
                        ]
                    },
                    new()
                    {
                        Id = "chemia", Name = "Chemia samochodowa", Children =
                        [
                            new() { Id = "oleje", Name = "Oleje silnikowe", Leaf = true },
                            new() { Id = "pozostala", Name = "Pozostała chemia samochodowa", Leaf = true }
                        ]
                    }
                ]
            },
            new()
            {
                Id = "dom", Name = "Dom i ogród", Children =
                [
                    new() { Id = "narzedzia", Name = "Narzędzia", Children = [new() { Id = "klucze", Name = "Klucze", Leaf = true }] }
                ]
            }
        ];

        private static List<AllegroCategoryNode> AllegroTree() =>
        [
            new() { CategoryId = 3, Name = "Motoryzacja" },
            new() { CategoryId = 99022, Name = "Części do maszyn i innych pojazdów", ParentId = 3 },
            new() { CategoryId = 252182, Name = "Części do maszyn rolniczych", ParentId = 99022 },
            new() { CategoryId = 252204, Name = "Do traktorów", ParentId = 252182 },
            new() { CategoryId = 252216, Name = "Układ hydrauliczny", ParentId = 252204 },
            new() { CategoryId = 5, Name = "Dom i Ogród" },
            new() { CategoryId = 1000, Name = "Narzędzia", ParentId = 5 },
            new() { CategoryId = 1001, Name = "Klucze", ParentId = 1000 },
            new() { CategoryId = 1002, Name = "Klucze nasadowe", ParentId = 1001 },
            new() { CategoryId = 2000, Name = "Chemia samochodowa", ParentId = 3 },
            new() { CategoryId = 2001, Name = "Preparaty do mycia", ParentId = 2000 },
            new() { CategoryId = 4000, Name = "Firma i usługi" }
        ];

        [Fact]
        public void Category_is_mapped_through_the_nearest_mapped_ancestor()
        {
            var mapper = new CategoryMapper(AllegroTree(), new Dictionary<string, string> { ["252204"] = TractorCategory }, InPostTree());

            Assert.Equal(TractorCategory, mapper.Map(252216).InPostCategoryId);
            Assert.Equal(TractorCategory, mapper.Map(252204).InPostCategoryId);
        }

        [Fact]
        public void Category_is_matched_by_name_path_when_inpost_tree_is_shallower()
        {
            var mapper = new CategoryMapper(AllegroTree(), new Dictionary<string, string>(), InPostTree());

            // "Dom i Ogród > Narzędzia > Klucze > Klucze nasadowe" -> liść InPost "Klucze" (wielkość liter bez znaczenia).
            Assert.Equal("klucze", mapper.Map(1002).InPostCategoryId);
            // Brak zgodnej nazwy pod rodzicem -> jego liść "Pozostała...".
            Assert.Equal("pozostala", mapper.Map(2001).InPostCategoryId);
        }

        [Fact]
        public void Branch_aliases_and_path_values_in_the_map_are_supported()
        {
            var tree = AllegroTree();
            tree.Add(new() { CategoryId = 2100, Name = "Chemia", ParentId = 3 });
            tree.Add(new() { CategoryId = 2101, Name = "Oleje silnikowe", ParentId = 2100 });

            var mapper = new CategoryMapper(
                tree,
                new Dictionary<string, string> { ["4000"] = "Dom i ogród > Narzędzia > Klucze" },
                InPostTree(),
                [new CategoryPathAlias { From = "Motoryzacja > Chemia", To = "Motoryzacja > Chemia samochodowa" }]);

            // Allegro "Motoryzacja > Chemia > Oleje silnikowe" → InPost "Motoryzacja > Chemia samochodowa > Oleje silnikowe".
            Assert.Equal("oleje", mapper.Map(2101).InPostCategoryId);
            // Wartość mapy podana jako ścieżka zamienia się na UUID kategorii.
            Assert.Equal("klucze", mapper.Map(4000).InPostCategoryId);
        }

        [Fact]
        public void Unknown_or_unmatched_category_has_a_reason()
        {
            var mapper = new CategoryMapper(AllegroTree(), new Dictionary<string, string>(), InPostTree());

            Assert.Null(mapper.Map(321505).InPostCategoryId);
            Assert.Contains("Brak nazwy kategorii", mapper.Map(321505).Reason);
            Assert.Contains("Brak odpowiednika", mapper.Map(4000).Reason);
            // Rodzic bez liścia "Pozostałe" nie może przyjąć oferty.
            Assert.Null(mapper.Map(252182).InPostCategoryId);
        }
    }
}
