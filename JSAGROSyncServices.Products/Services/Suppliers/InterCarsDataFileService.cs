using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    /// <summary>
    /// Pliki wymiany CSV Inter Cars (<c>data.webapi.intercars.eu</c>) - katalog na serwerze WWW
    /// z listingiem Apache, chroniony Basic Auth. Każdy plik to archiwum ZIP z jednym CSV
    /// rozdzielanym średnikami.
    ///
    /// Pliki powstają raz na dobę, a serwis synchronizuje się co kilka godzin, więc pobrane
    /// archiwum zostaje na dysku i jest pobierane ponownie dopiero wtedy, gdy na serwerze
    /// pojawi się nowsza wersja (inna nazwa, data lub rozmiar w listingu).
    /// </summary>
    public sealed class InterCarsDataFileService : IInterCarsDataFileService
    {
        public const string HttpClientName = "InterCarsData";

        /// <summary>Wiersz listingu Apache: nazwa pliku, data modyfikacji, rozmiar.</summary>
        private static readonly Regex ListingEntry = new(
            @"<a href=""(?<name>[^""?/][^""]*)""[^>]*>[^<]*</a>\s*</td>\s*<td[^>]*>\s*(?<date>[^<]*?)\s*</td>\s*<td[^>]*>\s*(?<size>[^<]*?)\s*</td>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string ProductInformationDirectory = "ProductInformation";
        private const string PicturesDirectory = "Pictures";

        private readonly SemaphoreSlim _downloadLock = new(1, 1);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InterCarsApiCredentials _credentials;
        private readonly ServiceContext _service;
        private readonly ILogger<InterCarsDataFileService> _logger;

        /// <summary>Lista SKU żyje tyle, ile plik na serwerze - trzymamy ją razem z jego sygnaturą.</summary>
        private string? _cachedSkusSignature;

        private HashSet<string>? _cachedSkus;

        public InterCarsDataFileService(
            IHttpClientFactory httpClientFactory,
            IOptions<InterCarsApiCredentials> credentials,
            ServiceContext service,
            ILogger<InterCarsDataFileService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _credentials = credentials.Value;
            _service = service;
            _logger = logger;
        }

        // ---------------------------------------------------------------- lista SKU

        public async Task<HashSet<string>?> GetAllowedSkusAsync(CancellationToken ct = default)
        {
            RemoteFile file;

            try
            {
                file = await GetNewestFileAsync(ProductInformationDirectory, ".csv.zip", ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Reading the Inter Cars {Directory} directory listing failed.", ProductInformationDirectory);
                return null;
            }

            await _downloadLock.WaitAsync(ct);

            try
            {
                if (_cachedSkus != null && _cachedSkusSignature == file.Signature)
                {
                    _logger.LogInformation("Inter Cars SKU whitelist: {Count} SKUs from {File} (cached).", _cachedSkus.Count, file.Name);
                    return _cachedSkus;
                }

                var sw = Stopwatch.StartNew();
                var skus = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                await ReadCsvAsync(file, (header, fields) =>
                {
                    var sku = Value(header, fields, "TOW_KOD");

                    if (!string.IsNullOrWhiteSpace(sku))
                        skus.Add(sku.Trim());
                }, ct);

                sw.Stop();

                if (skus.Count == 0)
                {
                    // Pusta lista przepuscilaby do bazy caly katalog Inter Cars - lepiej pominac cykl.
                    _logger.LogError("Inter Cars file {File} contains no SKUs - the whitelist was not updated.", file.Name);
                    return null;
                }

                _cachedSkus = skus;
                _cachedSkusSignature = file.Signature;

                _logger.LogInformation("Inter Cars SKU whitelist: {Count} SKUs from {File}. Took {Elapsed}.", skus.Count, file.Name, sw.Elapsed);

                return skus;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Downloading the Inter Cars SKU whitelist ({File}) failed.", file.Name);
                return null;
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        // ---------------------------------------------------------------- zdjecia

        public async Task<List<InterCarsProductImages>> GetImagesAsync(IReadOnlySet<string> skus, CancellationToken ct = default)
        {
            var result = new List<InterCarsProductImages>();

            if (skus.Count == 0)
                return result;

            RemoteFile file;

            try
            {
                file = await GetNewestFileAsync(PicturesDirectory, ".csv.zip", ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Reading the Inter Cars {Directory} directory listing failed.", PicturesDirectory);
                return result;
            }

            await _downloadLock.WaitAsync(ct);

            try
            {
                var sw = Stopwatch.StartNew();

                // Plik obejmuje caly katalog Inter Cars (ponad milion wierszy), a interesuje nas
                // tylko asortyment, ktory mamy w bazie - dlatego filtrujemy w trakcie czytania.
                var bySku = new Dictionary<string, List<(int Sort, string Url)>>(StringComparer.OrdinalIgnoreCase);
                var rows = 0;

                await ReadCsvAsync(file, (header, fields) =>
                {
                    rows++;

                    var sku = Value(header, fields, "TOW_KOD")?.Trim();
                    var url = Value(header, fields, "IMAGE_LINK")?.Trim();

                    if (string.IsNullOrWhiteSpace(sku) || string.IsNullOrWhiteSpace(url) || !skus.Contains(sku))
                        return;

                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                        return;

                    // SORTNR ustala kolejnosc zdjec w galerii. Brak wartosci wrzucamy na koniec.
                    var sort = int.TryParse(Value(header, fields, "SORTNR"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                        ? parsed
                        : int.MaxValue;

                    if (!bySku.TryGetValue(sku, out var urls))
                        bySku[sku] = urls = new List<(int, string)>();

                    urls.Add((sort, url));
                }, ct);

                sw.Stop();

                foreach (var (sku, urls) in bySku)
                {
                    result.Add(new InterCarsProductImages(
                        sku,
                        urls.OrderBy(u => u.Sort)
                            .Select(u => u.Url)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList()));
                }

                _logger.LogInformation(
                    "Inter Cars pictures file {File}: {Rows} rows read, {Products} of our products have photos ({Photos} in total). Took {Elapsed}.",
                    file.Name, rows, result.Count, result.Sum(r => r.Urls.Count), sw.Elapsed);

                return result;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Downloading the Inter Cars pictures file ({File}) failed.", file.Name);
                return result;
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        // ---------------------------------------------------------------- listing katalogu

        /// <param name="Url">Adres pliku względem bazowego adresu klienta, razem z katalogiem klienta.</param>
        /// <param name="Signature">Nazwa, data i rozmiar z listingu - po niej poznajemy, że plik się zmienił.</param>
        private sealed record RemoteFile(string Directory, string Name, string Url, string Signature);

        /// <summary>
        /// Najnowszy plik w katalogu. Pliki dzienne mają datę w nazwie, więc wystarczy porządek
        /// malejący po nazwie; katalogi z jednym, nadpisywanym plikiem (zdjęcia) zwracają go zawsze.
        /// </summary>
        private async Task<RemoteFile> GetNewestFileAsync(string directory, string extension, CancellationToken ct)
        {
            var http = CreateClient();

            using var response = await http.GetAsync($"{CustomerPath}/{directory}/", ct);
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);

            var files = ListingEntry.Matches(html)
                .Select(m => (
                    Name: Uri.UnescapeDataString(m.Groups["name"].Value),
                    Stamp: $"{m.Groups["date"].Value}|{m.Groups["size"].Value}"))
                .Where(f => f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (files.Count == 0)
                throw new InvalidOperationException($"Katalog {directory} nie zawiera żadnego pliku {extension}.");

            var newest = files[0];

            return new RemoteFile(
                directory,
                newest.Name,
                $"{CustomerPath}/{directory}/{Uri.EscapeDataString(newest.Name)}",
                $"{newest.Name}|{newest.Stamp}");
        }

        // ---------------------------------------------------------------- pobranie i odczyt

        /// <summary>
        /// Wywołuje <paramref name="onRow"/> dla każdego wiersza CSV z archiwum. Archiwum leży
        /// na dysku i jest pobierane tylko wtedy, gdy zmieniła się jego sygnatura z listingu -
        /// plik zdjęć ma kilkadziesiąt megabajtów, a zmienia się raz na kilka tygodni.
        /// </summary>
        private async Task ReadCsvAsync(RemoteFile file, Action<Dictionary<string, int>, string[]> onRow, CancellationToken ct)
        {
            var path = await EnsureDownloadedAsync(file, ct);

            using var archive = ZipFile.OpenRead(path);

            var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Archiwum {file.Name} nie zawiera pliku CSV.");

            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            Dictionary<string, int>? header = null;

            foreach (var fields in ReadRecords(reader))
            {
                ct.ThrowIfCancellationRequested();

                if (header == null)
                {
                    header = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                    // Zestaw kolumn zależy od umowy z dostawcą (część jest opcjonalna), więc
                    // odwołujemy się do nich po nazwie z nagłówka, a nie po pozycji.
                    for (var i = 0; i < fields.Length; i++)
                        header.TryAdd(fields[i].Trim(), i);

                    continue;
                }

                onRow(header, fields);
            }
        }

        private async Task<string> EnsureDownloadedAsync(RemoteFile file, CancellationToken ct)
        {
            // Obok katalogu ze zdjeciami, nie w nim - tam leza wylacznie foldery produktow.
            var directory = Path.GetFullPath(Path.Combine(_service.ImagesFolder, "..", "InterCarsData"));
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, $"{file.Directory}.csv.zip");
            var stampPath = Path.ChangeExtension(path, ".stamp");

            if (File.Exists(path) && File.Exists(stampPath))
            {
                var stamp = await File.ReadAllTextAsync(stampPath, ct);

                if (string.Equals(stamp, file.Signature, StringComparison.Ordinal))
                {
                    _logger.LogDebug("Inter Cars file {File} is already downloaded.", file.Name);
                    return path;
                }
            }

            var sw = Stopwatch.StartNew();
            var temporaryPath = path + ".part";

            var http = CreateClient();

            using (var response = await http.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();

                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = File.Create(temporaryPath);

                await source.CopyToAsync(target, ct);
            }

            // Podmiana na gotowym pliku - przerwane pobranie nie moze zostac uznane za aktualne.
            File.Move(temporaryPath, path, overwrite: true);
            await File.WriteAllTextAsync(stampPath, file.Signature, ct);

            sw.Stop();

            _logger.LogInformation(
                "Inter Cars file {File} downloaded ({Size:N1} MB). Took {Elapsed}.",
                file.Name, new FileInfo(path).Length / 1024d / 1024d, sw.Elapsed);

            return path;
        }

        private string CustomerPath => _credentials.DataCustomerNumber.Trim();

        private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

        // ---------------------------------------------------------------- parser CSV

        /// <summary>
        /// Wiersze pliku CSV rozdzielanego średnikami. Pole ujęte w cudzysłowy może zawierać
        /// separator i znak końca linii, a podwojony cudzysłów w środku oznacza jeden znak.
        /// Cudzysłów w środku pola niecytowanego (oznaczenie cala: <c>7/8"-14UNF</c>) zostaje
        /// zwykłym znakiem - dlatego cytowanie rozpoznajemy wyłącznie po pierwszym znaku pola.
        /// </summary>
        private static IEnumerable<string[]> ReadRecords(TextReader reader)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            var fieldStarted = false;
            var recordStarted = false;

            int read;

            while ((read = reader.Read()) >= 0)
            {
                var character = (char)read;

                if (quoted)
                {
                    if (character != '"')
                    {
                        field.Append(character);
                        continue;
                    }

                    // Podwojony cudzysłów wewnątrz pola cytowanego to jeden znak.
                    if (reader.Peek() == '"')
                    {
                        field.Append((char)reader.Read());
                        continue;
                    }

                    quoted = false;
                    continue;
                }

                switch (character)
                {
                    case '"' when !fieldStarted:
                        quoted = true;
                        fieldStarted = true;
                        recordStarted = true;
                        break;

                    case ';':
                        fields.Add(field.ToString());
                        field.Clear();
                        fieldStarted = false;
                        recordStarted = true;
                        break;

                    case '\r':
                        break;

                    case '\n':
                        if (recordStarted || field.Length > 0)
                        {
                            fields.Add(field.ToString());
                            yield return fields.ToArray();

                            fields.Clear();
                            field.Clear();
                        }

                        fieldStarted = false;
                        recordStarted = false;
                        break;

                    default:
                        field.Append(character);
                        fieldStarted = true;
                        recordStarted = true;
                        break;
                }
            }

            if (recordStarted || field.Length > 0)
            {
                fields.Add(field.ToString());
                yield return fields.ToArray();
            }
        }

        private static string? Value(Dictionary<string, int> header, string[] fields, string column) =>
            header.TryGetValue(column, out var index) && index < fields.Length ? fields[index] : null;
    }
}
