using System.Text;
using System.Text.RegularExpressions;

namespace Allegro.JSAGRO.InPost.ProductsService.Mappers
{
    /// <summary>
    /// Czytelność opisu w InPost. Opis z Allegro to akapity HTML bez odstępów - Allegro dokłada je
    /// stylami, a InPost pokazuje każdy akapit w następnej linii, więc nazwa, kod, opis i parametry
    /// zlewają się w jeden blok. Dlatego:
    /// <list type="bullet">
    /// <item>między sekcjami (akapit, lista) wstawiamy pustą linię,</item>
    /// <item>nagłówek listy ("Parametry/Wymiary:") zostaje przyklejony do swojej listy,</item>
    /// <item>długi akapit dzielimy na krótsze, po kilka zdań.</item>
    /// </list>
    /// </summary>
    public static class DescriptionFormatter
    {
        /// <summary>Akapit dłuższy niż tyle znaków dzielimy na kilka.</summary>
        public const int MaxParagraphLength = 350;

        private const string Spacer = "<br>";

        private static readonly Regex Block = new(@"<(p|ul|ol|h[1-6])\b[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Paragraph = new(@"^<p\b([^>]*)>(.*)</p>$", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Koniec zdania: kropka, wykrzyknik albo pytajnik, spacja i wielka litera. Nie dzielimy po
        /// skrótach i jednostkach ("np.", "maks.", "obr.", "kg.") - słowo przed kropką musi mieć
        /// co najmniej 4 litery albo być pisane wielkimi literami (nazwa, oznaczenie).
        /// </summary>
        private static readonly Regex SentenceEnd = new(
            @"(?<=(?:\p{L}{4,}|\p{Lu}{2,}|\d|\))[.!?])\s+(?=[\p{Lu}\d])",
            RegexOptions.Compiled);

        public static string Format(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return html;

            var blocks = Block.Matches(html).Select(m => m.Value.Trim()).ToList();

            // Opis bez rozpoznawalnych bloków (sam tekst) zostawiamy jak jest - nie zgadujemy struktury.
            if (blocks.Count == 0 || blocks.Sum(b => b.Length) < Tags.Replace(html, string.Empty).Length / 2)
                return html;

            var sb = new StringBuilder();

            for (var i = 0; i < blocks.Count; i++)
            {
                var block = SplitLongParagraph(blocks[i]);

                if (sb.Length > 0 && !IsListHeading(blocks[i - 1], block))
                    sb.Append(Spacer);

                sb.Append(block);
            }

            return sb.ToString();
        }

        /// <summary>Akapit kończący się dwukropkiem, a za nim lista - to nagłówek tej listy.</summary>
        private static bool IsListHeading(string previous, string current) =>
            current.StartsWith("<ul", StringComparison.OrdinalIgnoreCase) || current.StartsWith("<ol", StringComparison.OrdinalIgnoreCase)
                ? Tags.Replace(previous, string.Empty).TrimEnd().EndsWith(':')
                : false;

        /// <summary>Długi akapit → kilka akapitów po pełnych zdaniach, każdy do ~<see cref="MaxParagraphLength"/> znaków.</summary>
        private static string SplitLongParagraph(string block)
        {
            var match = Paragraph.Match(block);

            if (!match.Success || Tags.Replace(match.Groups[2].Value, string.Empty).Length <= MaxParagraphLength)
                return block;

            var attributes = match.Groups[1].Value;
            var sentences = SentenceEnd.Split(match.Groups[2].Value.Trim());

            if (sentences.Length < 2)
                return block;

            var chunks = new List<string>();
            var current = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (current.Length > 0 && Tags.Replace(current.ToString(), string.Empty).Length + sentence.Length > MaxParagraphLength)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                if (current.Length > 0)
                    current.Append(' ');

                current.Append(sentence);
            }

            if (current.Length > 0)
                chunks.Add(current.ToString());

            return string.Join(Spacer, chunks.Select(c => $"<p{attributes}>{c}</p>"));
        }
    }
}
