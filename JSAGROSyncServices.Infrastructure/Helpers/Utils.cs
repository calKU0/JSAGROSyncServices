using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace JSAGROSyncServices.Infrastructure.Helpers
{
    public static class Utils
    {
        public static byte[]? EnsureImageMinSize(byte[] image, int minWidth = 400, int minHeight = 400)
        {
            try
            {
                using var imageTemp = Image.Load(image);

                if (imageTemp.Width >= minWidth && imageTemp.Height >= minHeight)
                    return image; // already large enough

                // Calculate scale factor to meet minimum size
                double scaleX = (double)minWidth / imageTemp.Width;
                double scaleY = (double)minHeight / imageTemp.Height;
                double scale = Math.Max(scaleX, scaleY);

                int newWidth = (int)(imageTemp.Width * scale);
                int newHeight = (int)(imageTemp.Height * scale);

                imageTemp.Mutate(x => x.Resize(newWidth, newHeight));

                using var ms = new MemoryStream();
                imageTemp.Save(ms, new JpegEncoder());

                return ms.ToArray();
            }
            catch (Exception)
            {
                // Nieprzetwarzalny obrazek pomijamy - wywolujacy rozpozna to po null.
                return null;
            }
        }

        /// <summary>
        /// Skraca tresc odpowiedzi do wpisu w logu. Pelne body bledu z API to czesto kilka kilobajtow
        /// i przy kilkuset bledach zasypuje plik, a przyczyne widac w pierwszym zdaniu.
        /// </summary>
        public static string Shorten(string? body, int maxLength = 300) =>
            string.IsNullOrEmpty(body) || body.Length <= maxLength
                ? body ?? string.Empty
                : body[..maxLength] + "...";

        /// <summary>
        /// Lista kodow produktow do wpisu w logu: bez powtorzen, posortowana i ucieta do
        /// <paramref name="max"/> pozycji, zeby jeden wpis nie rozrosl sie do tysiecy kodow.
        /// </summary>
        public static string FormatCodes(IEnumerable<string?> codes, int max = 20)
        {
            var distinct = codes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (distinct.Count == 0)
                return "-";

            return distinct.Count <= max
                ? string.Join(", ", distinct)
                : string.Join(", ", distinct.Take(max)) + $" (+{distinct.Count - max} more)";
        }

        public static string GetContentTypeFromPath(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                _ => "application/octet-stream"
            };
        }
    }
}
