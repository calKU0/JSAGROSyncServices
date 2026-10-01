using JSAGROSyncServices.Infrastructure.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Pomocniki wpisów w logu. Log jest jedynym źródłem wiedzy o nocnych cyklach, więc wpis
    /// musi nazwać produkty, których dotyczy, i nie może rozrosnąć się do kilku kilobajtów.
    /// </summary>
    public class LogFormattingTests
    {
        [Fact]
        public void Short_body_is_logged_whole()
        {
            Assert.Equal("blad", Utils.Shorten("blad"));
        }

        [Fact]
        public void Long_body_is_cut_and_marked()
        {
            var shortened = Utils.Shorten(new string('x', 500));

            Assert.Equal(303, shortened.Length);
            Assert.EndsWith("...", shortened);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Empty_body_stays_empty(string? body)
        {
            Assert.Equal(string.Empty, Utils.Shorten(body));
        }

        [Fact]
        public void Codes_are_deduplicated_and_sorted()
        {
            // Ten sam produkt potrafi zgłosić kilka nieudanych zdjęć - w logu ma być raz.
            Assert.Equal("A1, B2, C3", Utils.FormatCodes(["C3", "A1", "B2", "a1"]));
        }

        [Fact]
        public void Too_many_codes_are_trimmed_with_a_counter()
        {
            var codes = Enumerable.Range(1, 25).Select(i => $"P{i:D2}").ToList();

            var formatted = Utils.FormatCodes(codes, max: 3);

            Assert.Equal("P01, P02, P03 (+22 more)", formatted);
        }

        [Fact]
        public void No_codes_is_a_dash()
        {
            Assert.Equal("-", Utils.FormatCodes([null, "", "   "]));
        }
    }
}
