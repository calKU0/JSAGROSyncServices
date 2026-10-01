using JSAGROSyncServices.Infrastructure.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Status publikacji decyduje o tym, czy wysyłamy komendę END. Pomyłka w którąkolwiek stronę
    /// jest kosztowna: zbędna komenda to żądanie co cykl dla tej samej oferty, a pominięta
    /// zostawia opublikowaną ofertę produktu, którego nie chcemy już sprzedawać.
    /// </summary>
    public class AllegroOfferStatusTests
    {
        [Theory]
        [InlineData("ACTIVE")]
        [InlineData("ACTIVATING")]
        // Allegro zwraca wielkimi literami, ale nasza baza przechowuje to, co przyszło.
        [InlineData("active")]
        [InlineData(" ACTIVE ")]
        public void Published_offers_can_be_ended(string status)
        {
            Assert.True(AllegroOfferStatus.IsPublished(status));
        }

        [Theory]
        // INACTIVE nigdy nie była wystawiona, ENDED jest już zakończona - komenda END
        // przechodzi bez błędu i nie zmienia statusu, więc oferta wracałaby tu w każdym cyklu.
        [InlineData("INACTIVE")]
        [InlineData("ENDED")]
        [InlineData("UNKNOWN")]
        [InlineData("")]
        [InlineData(null)]
        public void Unpublished_offers_are_not_ended(string? status)
        {
            Assert.False(AllegroOfferStatus.IsPublished(status));
        }

        [Theory]
        [InlineData("ENDED", true)]
        [InlineData("ended", true)]
        [InlineData("INACTIVE", false)]
        [InlineData("ACTIVE", false)]
        [InlineData(null, false)]
        public void Ended_is_recognised_separately(string? status, bool expected)
        {
            Assert.Equal(expected, AllegroOfferStatus.IsEnded(status));
        }
    }
}
