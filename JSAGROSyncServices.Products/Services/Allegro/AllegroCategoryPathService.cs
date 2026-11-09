using Dapper;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Infrastructure.Services;
using System.Data;

namespace JSAGROSyncServices.Products.Services.Allegro
{
    /// <summary>
    /// Słownik kategorii Allegro, w których stoją nasze oferty: nazwa i rodzic każdej kategorii
    /// aż do korzenia. Tabela AllegroCategories trzyma tylko gałąź części rolniczych (tam dobieramy
    /// kategorie produktom), a inne marketplace'y (InPost) mapują kategorie po pełnej ścieżce nazw.
    /// Pobieramy wyłącznie kategorie, których jeszcze nie znamy, więc po pierwszym przebiegu
    /// krok kosztuje pojedyncze zapytania.
    /// </summary>
    public class AllegroCategoryPathService
    {
        /// <summary>Zabezpieczenie przed pętlą przy uszkodzonych danych - drzewo Allegro ma kilka poziomów.</summary>
        private const int MaxDepth = 12;

        /// <summary>
        /// Kategorie, których Allegro nie zwróciło (usunięte) - pamiętane do końca dnia, żeby nie pytać
        /// o nie w każdym cyklu. Serwis działa jako długo żyjący proces, a ta klasa jest tworzona na cykl.
        /// </summary>
        private static readonly Dictionary<int, DateTime> NotFound = new();
        private static readonly object NotFoundLock = new();

        private readonly ILogger<AllegroCategoryPathService> _logger;
        private readonly AllegroApiClient _apiClient;
        private readonly DapperContext _context;

        public AllegroCategoryPathService(ILogger<AllegroCategoryPathService> logger, AllegroApiClient apiClient, DapperContext context)
        {
            _logger = logger;
            _apiClient = apiClient;
            _context = context;
        }

        public async Task SyncOfferCategoryPathsAsync(CancellationToken ct = default)
        {
            List<int> missing;
            HashSet<int> known;

            using (var connection = _context.CreateConnection())
            {
                missing = (await connection.QueryAsync<int>(new CommandDefinition(
                    "dbo.AllegroCategoryPaths_GetMissing",
                    commandType: CommandType.StoredProcedure,
                    cancellationToken: ct))).ToList();

                // Kategorie już zapisane kończą wspinaczkę po rodzicach - nie pytamy o nie ponownie.
                known = (await connection.QueryAsync<int>(new CommandDefinition(
                    "SELECT CategoryId FROM dbo.AllegroCategoryPaths",
                    cancellationToken: ct))).ToHashSet();
            }

            lock (NotFoundLock)
            {
                foreach (var stale in NotFound.Where(n => n.Value.Date < DateTime.Today).Select(n => n.Key).ToList())
                    NotFound.Remove(stale);

                missing = missing.Where(id => !NotFound.ContainsKey(id)).ToList();
            }

            if (missing.Count == 0)
                return;

            int saved = 0, failed = 0;

            foreach (var categoryId in missing)
            {
                var current = categoryId;

                for (var depth = 0; depth < MaxDepth && current != 0 && known.Add(current); depth++)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var category = await _apiClient.GetAsync<AllegroCategoryDetails>($"/sale/categories/{current}", ct);

                        if (category == null || string.IsNullOrWhiteSpace(category.Name))
                        {
                            lock (NotFoundLock)
                                NotFound[current] = DateTime.Now;

                            break;
                        }

                        var parentId = int.TryParse(category.Parent?.Id, out var p) ? p : (int?)null;

                        using (var connection = _context.CreateConnection())
                        {
                            await connection.ExecuteAsync(new CommandDefinition(
                                "dbo.AllegroCategoryPaths_Upsert",
                                new { CategoryId = current, category.Name, ParentId = parentId, category.Leaf },
                                commandType: CommandType.StoredProcedure,
                                cancellationToken: ct));
                        }

                        saved++;
                        current = parentId ?? 0;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        failed++;
                        _logger.LogError(ex, "Failed to fetch Allegro category {CategoryId}.", current);
                        break;
                    }
                }
            }

            _logger.LogInformation("Allegro category paths - missing: {Missing}, saved: {Saved}, failed: {Failed}.", missing.Count, saved, failed);
        }

        private sealed class AllegroCategoryDetails
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
            public bool Leaf { get; set; }
            public ParentRef? Parent { get; set; }
        }

        private sealed class ParentRef
        {
            public string? Id { get; set; }
        }
    }
}
