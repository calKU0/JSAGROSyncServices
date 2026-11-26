using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Wycinanie zdjęć zablokowanych przez Allegro - tylko tych, z galerii, opisu i zdjęć produktu.</summary>
    public class BlockedImageFilterTests
    {
        private const string Logo = "https://a.allegroimg.com/original/11359c/logo";
        private const string A = "https://a.allegroimg.com/original/aa/a";
        private const string B = "https://a.allegroimg.com/original/bb/b";
        private const string C = "https://a.allegroimg.com/original/cc/c";

        private static ProductOfferRequest Request(string? productId = "p1") => new()
        {
            ProductSet = new List<ProductSet> { new() { ProductObject = new ProductObject { Id = productId } } },
            Images = new List<string> { A, B, Logo },
            Description = new Description
            {
                Sections = new List<Section>
                {
                    new() { SectionItems = new List<SectionItem> { new() { Type = "TEXT", Content = "<p>x</p>" }, new() { Type = "IMAGE", Url = A } } },
                    new() { SectionItems = new List<SectionItem> { new() { Type = "IMAGE", Url = Logo } } }
                }
            }
        };

        private static readonly IReadOnlySet<string> Blocked = new HashSet<string> { Logo };

        [Fact]
        public void RemovesOnlyBlockedImageFromGalleryAndDescription()
        {
            var request = Request();

            var removed = BlockedImageFilter.Apply(request, Blocked, new Dictionary<string, List<string>>());

            Assert.Equal(new[] { A, B }, request.Images);
            Assert.Single(request.Description!.Sections);
            Assert.Equal(2, removed);
        }

        [Fact]
        public void SendsCatalogImagesWithoutBlockedOneOnlyWhenCatalogHasIt()
        {
            var withLogo = Request();
            BlockedImageFilter.Apply(withLogo, Blocked, new Dictionary<string, List<string>> { ["p1"] = new() { C, Logo } });
            Assert.Equal(new[] { C }, withLogo.ProductSet[0].ProductObject!.Images);

            var clean = Request();
            BlockedImageFilter.Apply(clean, Blocked, new Dictionary<string, List<string>> { ["p1"] = new() { C } });
            Assert.Null(clean.ProductSet[0].ProductObject!.Images);
        }

        [Fact]
        public void UsesOwnCleanGalleryWhenAllCatalogImagesAreBlocked()
        {
            var request = Request();

            BlockedImageFilter.Apply(request, new HashSet<string> { Logo, C }, new Dictionary<string, List<string>> { ["p1"] = new() { C, Logo } });

            Assert.Equal(new[] { A, B }, request.ProductSet[0].ProductObject!.Images);
        }

        [Fact]
        public void UsesCleanCatalogImagesAsGalleryWhenWholeOwnGalleryIsBlocked()
        {
            var request = Request();

            BlockedImageFilter.Apply(request, new HashSet<string> { A, B, Logo }, new Dictionary<string, List<string>> { ["p1"] = new() { C, Logo } });

            Assert.Equal(new[] { C }, request.Images);
            Assert.Equal(new[] { C }, request.ProductSet[0].ProductObject!.Images);
        }

        [Fact]
        public void KeepsGalleryWhenEverythingWouldBeRemoved()
        {
            var request = new ProductOfferRequest { Images = new List<string> { Logo } };

            BlockedImageFilter.Apply(request, Blocked, new Dictionary<string, List<string>>());

            Assert.Equal(new[] { Logo }, request.Images);
        }

        [Fact]
        public void CandidatesStartWithCatalogImagesFromTheEnd()
        {
            var candidates = BlockedImageFilter.Candidates(Request(), new Dictionary<string, List<string>> { ["p1"] = new() { C, Logo } }, new HashSet<string>());

            Assert.Equal(new[] { Logo, C, B, A }, candidates);
        }

        [Fact]
        public void CandidatesSkipKnownBlocked()
        {
            var candidates = BlockedImageFilter.Candidates(Request(), new Dictionary<string, List<string>>(), Blocked);

            Assert.DoesNotContain(Logo, candidates);
        }
    }
}
