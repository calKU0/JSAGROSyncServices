using Allegro.JSAGRO.InPost.ProductsService.Models;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using Dapper;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using System.Data;
using System.Text.Json;

namespace Allegro.JSAGRO.InPost.ProductsService.Repositories
{
    public class InPostOfferRepository
    {
        private const int CommandTimeoutSeconds = 600;

        private readonly DapperContext _context;

        public InPostOfferRepository(DapperContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Aktywne oferty z wybranych cenników razem z produktem, opisem i parametrami.
        /// <paramref name="offerIds"/> = null oznacza wszystkie oferty.
        /// </summary>
        public async Task<List<InPostSourceOffer>> GetCandidatesAsync(
            AllegroAccount account,
            IEnumerable<DeliveryPriceListSettings> deliveryLists,
            IReadOnlyCollection<string>? offerIds,
            CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var command = new CommandDefinition(
                "dbo.InPostOffers_GetCandidates",
                new
                {
                    Account = (int)account,
                    DeliveryLists = JsonSerializer.Serialize(deliveryLists.Select(l => new { name = l.Name, company = (int)l.IntegrationCompany })),
                    OfferIds = offerIds is { Count: > 0 } ? JsonSerializer.Serialize(offerIds) : null
                },
                commandType: CommandType.StoredProcedure,
                commandTimeout: CommandTimeoutSeconds,
                cancellationToken: ct);

            using var grid = await connection.QueryMultipleAsync(command);

            var offers = (await grid.ReadAsync<CandidateRow>()).Select(ToOffer).ToList();
            var byOffer = offers.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
            var byProduct = offers.ToLookup(o => o.Product.Id);

            foreach (var description in await grid.ReadAsync<AllegroOfferDescription>())
            {
                if (byOffer.TryGetValue(description.OfferId, out var offer))
                    offer.Descriptions.Add(description);
            }

            foreach (var row in await grid.ReadAsync<OfferAttributeRow>())
            {
                if (!byOffer.TryGetValue(row.OfferId, out var offer) || string.IsNullOrWhiteSpace(row.Name))
                    continue;

                var values = ParseValues(row.ValuesJson);

                if (values.Count > 0)
                    offer.OfferParameters.Add(new NamedValues { Name = row.Name, Values = values });
            }

            foreach (var row in await grid.ReadAsync<ProductParameter>())
            {
                foreach (var offer in byProduct[row.ProductId])
                    offer.Product.Parameters.Add(row);
            }

            foreach (var row in await grid.ReadAsync<ProductSpecification>())
            {
                foreach (var offer in byProduct[row.ProductId])
                    (offer.Product.Specifications ??= new List<ProductSpecification>()).Add(row);
            }

            foreach (var row in await grid.ReadAsync<SupplierCategoryRow>())
            {
                foreach (var offer in byProduct[row.ProductId])
                {
                    if (!string.IsNullOrWhiteSpace(row.SourceKey))
                        offer.SupplierCategoryPaths.Add(row.SourceKey);
                }
            }

            return offers;
        }

        /// <summary>
        /// Słownik kategorii Allegro (id → nazwa, rodzic): gałąź części rolniczych z AllegroCategories
        /// i kategorie ofert zapisane przez krok "Allegro category paths" serwisu JSAGRO-Gąska.
        /// </summary>
        public async Task<List<AllegroCategoryNode>> GetAllegroCategoryNodesAsync(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var rows = await connection.QueryAsync<AllegroCategoryNode>(new CommandDefinition(
                """
                SELECT TRY_CAST(c.CategoryId AS INT) AS CategoryId, c.Name, TRY_CAST(p.CategoryId AS INT) AS ParentId
                FROM dbo.AllegroCategories c
                LEFT JOIN dbo.AllegroCategories p ON p.Id = c.ParentId
                WHERE TRY_CAST(c.CategoryId AS INT) IS NOT NULL
                UNION ALL
                SELECT CategoryId, Name, ParentId
                FROM dbo.AllegroCategoryPaths
                """,
                cancellationToken: ct));

            return rows.ToList();
        }

        public async Task<List<TrackedInPostOffer>> GetTrackedAsync(AllegroAccount account, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            var rows = await connection.QueryAsync<TrackedInPostOffer>(new CommandDefinition(
                "dbo.InPostOffers_GetTracked",
                new { Account = (int)account },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

            return rows.ToList();
        }

        public async Task UpsertAsync(
            AllegroAccount account,
            string allegroOfferId,
            string externalId,
            Guid? inPostOfferId = null,
            Guid? lastCommandId = null,
            string? status = null,
            decimal? price = null,
            int? stock = null,
            string? validationErrors = null,
            string? lastError = null,
            string? contentHash = null,
            bool clearPending = false,
            CancellationToken ct = default)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.InPostOffers_Upsert",
                new
                {
                    AllegroOfferId = allegroOfferId,
                    Account = (int)account,
                    ExternalId = externalId,
                    InPostOfferId = inPostOfferId,
                    LastCommandId = lastCommandId,
                    Status = status,
                    Price = price,
                    Stock = stock,
                    ValidationErrors = validationErrors,
                    LastError = lastError,
                    ContentHash = contentHash,
                    ClearPending = clearPending
                },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
        }

        /// <summary>Wartość wysłana zbiorczo ("price" / "stock"), czekająca na wynik komendy.</summary>
        public async Task SetPendingAsync(string allegroOfferId, string kind, decimal value, Guid commandId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.InPostOffers_SetPending",
                new { AllegroOfferId = allegroOfferId, Kind = kind, Value = value, CommandId = commandId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
        }

        /// <summary>Wynik komendy: sukces zatwierdza wartość "w drodze", porażka zostawia starą i zapisuje powód.</summary>
        public async Task ResolvePendingAsync(string allegroOfferId, string kind, Guid commandId, bool success, string? error, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.InPostOffers_ResolvePending",
                new { AllegroOfferId = allegroOfferId, Kind = kind, CommandId = commandId, Success = success, Error = error },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
        }

        /// <summary>Przepięcie oferty InPost na inną ofertę Allegro tego samego produktu.</summary>
        public async Task ReassignAsync(string fromAllegroOfferId, string toAllegroOfferId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();

            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.InPostOffers_Reassign",
                new { FromAllegroOfferId = fromAllegroOfferId, ToAllegroOfferId = toAllegroOfferId },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));
        }

        private static List<string> ParseValues(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                return (JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Select(v => v.Trim())
                    .ToList();
            }
            catch (JsonException)
            {
                return new List<string> { json.Trim() };
            }
        }

        private static InPostSourceOffer ToOffer(CandidateRow row) => new()
        {
            Id = row.Id,
            ExternalId = row.ExternalId ?? string.Empty,
            Name = row.Name ?? string.Empty,
            CategoryId = row.CategoryId,
            Price = row.Price,
            Stock = row.Stock,
            Status = row.Status ?? string.Empty,
            DeliveryName = row.DeliveryName ?? string.Empty,
            AllegroProductId = row.AllegroProductId,
            Images = row.Images,
            Weight = row.Weight,
            HandlingTime = row.HandlingTime,
            ResponsibleProducer = row.ResponsibleProducer,
            ResponsiblePerson = row.ResponsiblePerson,
            VisitsCount = row.VisitsCount,
            InPostOfferId = row.InPostOfferId,
            LastCommandId = row.LastCommandId,
            InPostStatus = row.InPostStatus,
            InPostPrice = row.InPostPrice,
            InPostStock = row.InPostStock,
            Product = new RolmarProduct
            {
                Id = row.ProductId,
                Code = row.ProductCode ?? string.Empty,
                Name = row.ProductName ?? string.Empty,
                Description = row.ProductDescription,
                Ean = row.Ean,
                Weight = (float)row.ProductWeight,
                Fits = row.Fits,
                Substitutes = row.Substitutes,
                InStock = (float)row.InStock,
                Unit = row.Unit ?? string.Empty,
                PriceNet = row.PriceNet,
                PriceGross = row.PriceGross,
                AcceptedPriceGross = row.AcceptedPriceGross,
                PriceDropDetectedAt = row.PriceDropDetectedAt,
                DeliveryType = row.DeliveryType,
                ArchivedAt = row.ArchivedAt,
                Package = row.Package,
                ShippingClass = row.ShippingClass,
                SupplierName = row.SupplierName,
                Specifications = new List<ProductSpecification>()
            }
        };

        private sealed class CandidateRow
        {
            public string Id { get; set; } = string.Empty;
            public string? ExternalId { get; set; }
            public string? Name { get; set; }
            public int CategoryId { get; set; }
            public decimal Price { get; set; }
            public int Stock { get; set; }
            public string? Status { get; set; }
            public string? DeliveryName { get; set; }
            public string? AllegroProductId { get; set; }
            public string? Images { get; set; }
            public decimal Weight { get; set; }
            public string? HandlingTime { get; set; }
            public string? ResponsibleProducer { get; set; }
            public string? ResponsiblePerson { get; set; }
            public int VisitsCount { get; set; }
            public int ProductId { get; set; }
            public string? ProductCode { get; set; }
            public string? ProductName { get; set; }
            public string? ProductDescription { get; set; }
            public string? Ean { get; set; }
            public double ProductWeight { get; set; }
            public string? Fits { get; set; }
            public string? Substitutes { get; set; }
            public double InStock { get; set; }
            public string? Unit { get; set; }
            public decimal PriceNet { get; set; }
            public decimal PriceGross { get; set; }
            public decimal? AcceptedPriceGross { get; set; }
            public DateTime? PriceDropDetectedAt { get; set; }
            public int DeliveryType { get; set; }
            public DateTime? ArchivedAt { get; set; }
            public decimal Package { get; set; }
            public string? ShippingClass { get; set; }
            public string? SupplierName { get; set; }
            public Guid? InPostOfferId { get; set; }
            public Guid? LastCommandId { get; set; }
            public string? InPostStatus { get; set; }
            public decimal? InPostPrice { get; set; }
            public int? InPostStock { get; set; }
        }

        private sealed class OfferAttributeRow
        {
            public string OfferId { get; set; } = string.Empty;
            public string? Name { get; set; }
            public string? Type { get; set; }
            public string? ValuesJson { get; set; }
        }

        private sealed class SupplierCategoryRow
        {
            public int ProductId { get; set; }
            public string? SourceKey { get; set; }
        }
    }
}
