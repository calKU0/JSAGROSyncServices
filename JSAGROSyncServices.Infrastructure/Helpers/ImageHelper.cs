using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Infrastructure.Helpers
{
    public record ImageSyncResult(int Downloaded, int Reused, int Failed, int Removed, string? LastError = null);

    public static class ImageHelper
    {
        private const string UrlHashedFilePattern = @"^image_\d+_(?<hash>[0-9a-f]{10})\.";

        /// <summary>
        /// Pobiera tylko te zdjęcia, których jeszcze nie ma na dysku. Nazwa pliku zawiera skrót adresu URL,
        /// dzięki czemu wiadomo które zdjęcie zostało już pobrane, a numer w nazwie pilnuje kolejności zdjęć.
        /// Pliki zdjęć, których nie ma już u dostawcy, są usuwane.
        /// </summary>
        public static async Task<ImageSyncResult> SaveNewImagesAsync(
                    HttpClient httpClient,
                    List<string?> urls,
                    int productId,
                    string baseDirectory,
                    CancellationToken ct = default)
        {
            if (urls == null || urls.Count == 0 || productId <= 0)
                return new ImageSyncResult(0, 0, 0, 0);

            var productFolder = Path.Combine(baseDirectory, productId.ToString());
            Directory.CreateDirectory(productFolder);

            var expectedFiles = new List<(string Url, string Hash, string FileName)>();
            var expectedHashes = new HashSet<string>();
            int position = 1;

            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult) || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
                    continue;

                var hash = GetUrlHash(url);

                if (!expectedHashes.Add(hash))
                    continue;

                var extension = Path.GetExtension(url);

                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5)
                    extension = ".jpg";

                expectedFiles.Add((url, hash, $"image_{position:D3}_{hash}{extension}"));
                position++;
            }

            var downloadedByHash = Directory
                .EnumerateFiles(productFolder)
                .Select(path => (Path: path, Match: Regex.Match(Path.GetFileName(path), UrlHashedFilePattern)))
                .Where(x => x.Match.Success)
                .GroupBy(x => x.Match.Groups["hash"].Value)
                .ToDictionary(g => g.Key, g => g.First().Path);

            int downloaded = 0;
            int reused = 0;
            int failed = 0;
            string? lastError = null;

            foreach (var expected in expectedFiles)
            {
                var targetPath = Path.Combine(productFolder, expected.FileName);

                if (downloadedByHash.TryGetValue(expected.Hash, out var existingPath))
                {
                    try
                    {
                        // Zdjęcie jest już pobrane, może się jedynie zmienić jego pozycja w kolejności.
                        if (!string.Equals(existingPath, targetPath, StringComparison.OrdinalIgnoreCase))
                            File.Move(existingPath, targetPath, overwrite: true);

                        reused++;
                        continue;
                    }
                    catch
                    {
                        // Nie udało się przenieść - pobieramy zdjęcie jeszcze raz.
                    }
                }

                try
                {
                    var imgBytes = await httpClient.GetByteArrayAsync(expected.Url, ct);
                    await File.WriteAllBytesAsync(targetPath, imgBytes, ct);
                    downloaded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    lastError = ex.Message;
                }
            }

            int removed = 0;

            // Sprzatamy dopiero, gdy komplet zdjec faktycznie lezy na dysku.
            // Inaczej nieudane pobranie (np. zdechly adres u dostawcy) skasowaloby to, co juz mamy.
            if (failed == 0)
            {
                var expectedNames = expectedFiles
                    .Select(f => f.FileName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var file in Directory.EnumerateFiles(productFolder).ToList())
                {
                    if (expectedNames.Contains(Path.GetFileName(file)))
                        continue;

                    try
                    {
                        File.Delete(file);
                        removed++;
                    }
                    catch
                    {
                    }
                }
            }

            return new ImageSyncResult(downloaded, reused, failed, removed, lastError);
        }

        private static string GetUrlHash(string url)
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(url));
            return Convert.ToHexString(bytes)[..10].ToLowerInvariant();
        }

        public static List<string> GetImageFiles(string folderPath, int productId)
        {
            if (!Directory.Exists(folderPath) || productId <= 0)
                return new List<string>();

            var productFolder = Path.Combine(folderPath, productId.ToString());
            if (!Directory.Exists(productFolder))
                return new List<string>();

            return Directory.EnumerateFiles(productFolder)
                .Where(f =>
                    f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
