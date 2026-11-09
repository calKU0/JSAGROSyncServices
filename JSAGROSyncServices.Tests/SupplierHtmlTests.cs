using JSAGROSyncServices.Products.Helpers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Opisy dostawcy z HTML-em. Wcześniej cały opis szedł przez HtmlEncode i kupujący widział
    /// na Allegro surowe "&lt;b&gt;" i "&lt;br /&gt;" (ok. 900 ofert Rolmara na każdym koncie).
    /// </summary>
    public class SupplierHtmlTests
    {
        [Fact]
        public void Bold_and_line_breaks_become_paragraphs()
        {
            var result = SupplierHtml.ToBlocks("<b>Drążek kierowniczy 1370mm WARYŃSKI</b><br /> <b>Przeguby WARYŃSKI </b> to jakość <b>pierwszego montażu</b>.");

            Assert.Equal("<p><b>Drążek kierowniczy 1370mm WARYŃSKI</b></p><p><b>Przeguby WARYŃSKI </b> to jakość <b>pierwszego montażu</b>.</p>", result);
        }

        [Fact]
        public void Dash_lines_become_a_list()
        {
            var result = SupplierHtml.ToBlocks("<b>HARTOWANIE</b> gwarantuje</br>\n- dłuższa żywotność</br>\n- zwiększona odporność na ścieranie</br>\nKoniec.");

            Assert.Equal("<p><b>HARTOWANIE</b> gwarantuje</p><ul><li>dłuższa żywotność</li><li>zwiększona odporność na ścieranie</li></ul><p>Koniec.</p>", result);
        }

        [Fact]
        public void Plain_text_is_encoded_and_no_raw_tag_survives()
        {
            var result = SupplierHtml.ToBlocks("Średnica < 25 mm & gwint 1/2\" <span style=\"x\">BSP</span>");

            Assert.Equal("<p>Średnica &lt; 25 mm &amp; gwint 1/2&quot; BSP</p>", result);
        }

        [Fact]
        public void Bold_spanning_a_line_break_is_closed_and_reopened()
        {
            Assert.Equal("<p><b>UWAGA!!!</b></p><p><b>Nowa konstrukcja</b></p>", SupplierHtml.ToBlocks("<b>UWAGA!!!<br>Nowa konstrukcja</b>"));
        }

        [Fact]
        public void Label_goes_into_the_first_paragraph()
        {
            Assert.Equal("<p><b>Opis: </b>Pompki zasilające <b>GWARANCJA</b></p>", SupplierHtml.ToLabelledBlocks("Opis:", "Pompki zasilające <b>GWARANCJA</b>"));
            Assert.Equal("<p><b>Opis: </b></p><ul><li>jeden</li></ul>", SupplierHtml.ToLabelledBlocks("Opis:", "- jeden"));
            Assert.Equal(string.Empty, SupplierHtml.ToLabelledBlocks("Opis:", "  "));
        }

        [Fact]
        public void Tags_already_escaped_in_saved_offer_description_are_repaired()
        {
            // Tak wygląda opis oferty Allegro wystawionej przed poprawką - InPost dostaje go z bazy.
            var saved = "<p><b>Kod produktu: </b>84450042U</p><p><b>Opis: </b>Sworznie &lt;b&gt;URSUS&lt;/b&gt; hartowane&lt;/br&gt;\n- dłuższa żywotność&lt;br /&gt;</p>";

            var result = SupplierHtml.ToBlocks(saved, decodeEscapedTags: true);

            Assert.Equal("<p><b>Kod produktu: </b>84450042U</p><p><b>Opis: </b>Sworznie <b>URSUS</b> hartowane</p><ul><li>dłuższa żywotność</li></ul>", result);
            Assert.DoesNotContain("&lt;", result);
        }

        [Fact]
        public void Allegro_description_without_supplier_tags_is_kept()
        {
            var html = "<p><b>Kod produktu: </b>W-3071</p><p><b>Parametry/Wymiary:</b></p><ul><li><b>Waga</b>: 6,5 kg</li></ul>";

            Assert.Equal(html, SupplierHtml.ToBlocks(html, decodeEscapedTags: true));
        }
    }
}
