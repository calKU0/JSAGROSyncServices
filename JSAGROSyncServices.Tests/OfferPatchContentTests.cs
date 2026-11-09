using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Patch ze stanem i ceną, gdy treść oferty się nie zmieniła.</summary>
    public class OfferPatchContentTests
    {
        private static ProductOfferRequest Request(int stock = 5, string price = "10.00", string status = "ACTIVE",
            string description = "<p>Opis</p>", string image = "https://a.allegroimg.com/1.jpg") => new()
        {
            ProductSet = [new ProductSet { ProductObject = new ProductObject { Id = "abc" }, Quantity = new Quantity { Value = 1 } }],
            Stock = new Stock { Available = stock, Unit = "UNIT" },
            SellingMode = new SellingMode { Format = "BUY_NOW", Price = new Price { Amount = price, Currency = "PLN" } },
            Publication = new Publication { Status = status },
            Images = [image],
            Description = new Description
            {
                Sections = [new Section { SectionItems = [new SectionItem { Type = "TEXT", Content = description }] }]
            },
            Parameters = [new Parameter { Name = "Stan", Values = ["Nowy"] }]
        };

        [Fact]
        public void Stock_price_and_status_do_not_change_the_content_hash()
        {
            Assert.Equal(
                OfferPatchContent.ContentHash(Request()),
                OfferPatchContent.ContentHash(Request(stock: 1, price: "99.99", status: "ENDED")));
        }

        [Fact]
        public void Description_or_images_change_the_content_hash()
        {
            var hash = OfferPatchContent.ContentHash(Request());

            Assert.NotEqual(hash, OfferPatchContent.ContentHash(Request(description: "<p>Nowy opis</p>")));
            Assert.NotEqual(hash, OfferPatchContent.ContentHash(Request(image: "https://a.allegroimg.com/2.jpg")));
        }

        [Fact]
        public void Price_and_stock_patch_carries_only_volatile_fields_and_the_product()
        {
            var full = Request();
            var slim = OfferPatchContent.PriceAndStockOnly(full);

            Assert.Same(full.Stock, slim.Stock);
            Assert.Same(full.SellingMode, slim.SellingMode);
            Assert.Same(full.Publication, slim.Publication);
            Assert.Same(full.ProductSet, slim.ProductSet);
            Assert.Null(slim.Images);
            Assert.Null(slim.Description);
            Assert.Null(slim.Parameters);
            Assert.Null(slim.Delivery);
        }

        [Fact]
        public void Unchanged_content_needs_a_matching_recent_hash()
        {
            var hash = OfferPatchContent.ContentHash(Request());
            var now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

            Assert.True(OfferPatchContent.IsUnchanged(hash, new OfferContentState(hash, now.AddDays(-1)), now));
            Assert.False(OfferPatchContent.IsUnchanged(hash, null, now));
            Assert.False(OfferPatchContent.IsUnchanged(hash, new OfferContentState("other", now.AddDays(-1)), now));
            // Raz na tydzień pełna struktura, choćby treść się nie zmieniła.
            Assert.False(OfferPatchContent.IsUnchanged(hash, new OfferContentState(hash, now.AddDays(-8)), now));
        }
    }
}
