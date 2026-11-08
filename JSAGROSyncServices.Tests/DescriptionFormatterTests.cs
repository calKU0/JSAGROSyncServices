using Allegro.JSAGRO.InPost.ProductsService.Mappers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Opis InPost: odstępy między sekcjami i podział długiego akapitu.</summary>
    public class DescriptionFormatterTests
    {
        private const string Orbitrol =
            "<p><b>Orbitrol hydrauliczny W-ORBS-OR 160 cm3/obr </b></p>" +
            "<p><b>Kod produktu: </b>W-ORBS-160-OR</p>" +
            "<p><b>Opis: </b>Hydrostatyczny zawór skrętu W-ORBS-OR 160 cm3/obr z zaworami Waryński. " +
            "Orbitrole serii W-ORBS (OR) mają zwartą i solidną konstrukcję, pozwalającą na lekką i płynną pracę układu kierowniczego. " +
            "Zasada działania systemu OPEN CENTER REACTION - popularny między innymi w ciągnikach sadowniczych poruszających się w trudnym do jazdy terenie. " +
            "Kierowca/operator ciągnika przejeżdżając między pryzmami czuje przenoszenie drgań na swojej kierownicy. " +
            "Dzięku temu ma czas na korektę toru jazdy, co daje jednocześnie możliwość nieuszkodzenia upraw. " +
            "Wspomaganie hydrauliczne gwarantuje wysoką precyzję kierowania sprzętem przy zachowaniu niewielkiej awaryjności.</p>" +
            "<p><b>Parametry/Wymiary:</b></p>" +
            "<ul><li><b>Waga</b>: 6,5 kg</li><li><b>Maks. ciśnienie</b>: 17,5/175 Mpa/bar</li></ul>" +
            "<p><b>Zastosowanie: </b>Ursus C-360</p>";

        [Fact]
        public void Sections_are_separated_by_an_empty_line_and_list_heading_stays_with_its_list()
        {
            var result = DescriptionFormatter.Format(Orbitrol);

            Assert.Contains("W-ORBS-160-OR</p><br><p><b>Opis: </b>", result);
            Assert.Contains("<p><b>Parametry/Wymiary:</b></p><ul>", result);
            Assert.Contains("</ul><br><p><b>Zastosowanie: </b>", result);
        }

        [Fact]
        public void Long_paragraph_is_split_on_sentence_boundaries()
        {
            var result = DescriptionFormatter.Format(Orbitrol);

            var paragraphs = result.Split("<p>").Where(p => p.Length > 0).ToList();
            var description = paragraphs.SkipWhile(p => !p.StartsWith("<b>Opis")).TakeWhile(p => !p.StartsWith("<b>Parametry")).ToList();

            Assert.True(description.Count >= 2, result);
            Assert.All(description, p => Assert.True(p.Length <= DescriptionFormatter.MaxParagraphLength + 40, p));

            // Treść zostaje w całości i w tej samej kolejności - zmieniają się tylko podziały.
            string Text(string html) => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Replace("  ", " ");
            Assert.Equal(
                string.Join(" ", Text(Orbitrol).Split(' ', StringSplitOptions.RemoveEmptyEntries)),
                string.Join(" ", Text(result).Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        [Theory]
        [InlineData("<p>Średnica 25 mm, np. do ciągnika. Maks. ciśnienie 175 bar.</p>")]
        public void Short_paragraph_and_abbreviations_are_left_alone(string html) =>
            Assert.Equal(html, DescriptionFormatter.Format(html));

        [Fact]
        public void Plain_text_without_html_blocks_is_not_touched() =>
            Assert.Equal("Zwykły tekst bez akapitów.", DescriptionFormatter.Format("Zwykły tekst bez akapitów."));
    }
}
