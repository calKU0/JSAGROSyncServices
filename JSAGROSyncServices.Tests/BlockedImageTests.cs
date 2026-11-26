using JSAGROSyncServices.Products.Services.Allegro;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Rozpoznanie odrzucenia oferty za zablokowane zdjęcie - wtedy aktualizujemy sam stan i cenę.
    /// </summary>
    public class BlockedImageTests
    {
        [Theory]
        [InlineData("{\"errors\":[{\"code\":\"ConstraintViolationException.OfferValidation\",\"path\":\"name\",\"userMessage\":\"Zdjęcie zostało zablokowane\"}]}", true)]
        [InlineData("{\"errors\":[{\"code\":\"ConstraintViolationException.OfferValidation\",\"userMessage\":\"Zdj\\u0119cie zosta\\u0142o zablokowane\"}]}", true)]
        [InlineData("{\"errors\":[{\"code\":\"ConstraintViolationException.GallerySize\",\"userMessage\":\"Musisz dodać przynajmniej 1 zdjęcie.\"}]}", false)]
        public void RecognizesBlockedImage(string body, bool expected) =>
            Assert.Equal(expected, AllegroOfferService.IsBlockedImage(body));

        [Theory]
        [InlineData("a", "b", true)]
        [InlineData("a", "A", false)]
        [InlineData("a", " a ", false)]
        [InlineData(null, "b", false)]
        [InlineData("a", null, false)]
        [InlineData("", "b", false)]
        [InlineData("NULL", "b", false)]
        [InlineData("a", "NULL", false)]
        public void DetectsDifferentCatalogLinks(string? offerProductId, string? productAllegroId, bool expected) =>
            Assert.Equal(expected, AllegroOfferService.LinksDiffer(offerProductId, productAllegroId));

        private static readonly System.Text.Json.JsonSerializerOptions AllegroJson = new()
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        [Fact]
        public void RemovedCompatibilityListIsSentAsExplicitNull()
        {
            var request = new JSAGROSyncServices.Contracts.DTOs.Allegro.ProductOfferRequest
            {
                CompatibilityList = new JSAGROSyncServices.Contracts.DTOs.Allegro.CompatibilityList()
            };

            request.RemoveCompatibilityList();
            var json = System.Text.Json.JsonSerializer.Serialize(request, AllegroJson);

            Assert.Contains("\"compatibilityList\":null", json);
            Assert.DoesNotContain("\"items\"", json);
        }

        [Fact]
        public void UntouchedCompatibilityListIsOmitted()
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new JSAGROSyncServices.Contracts.DTOs.Allegro.ProductOfferRequest(), AllegroJson);

            Assert.DoesNotContain("compatibilityList", json);
        }

    }
}
