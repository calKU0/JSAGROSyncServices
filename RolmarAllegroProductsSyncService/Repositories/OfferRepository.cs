using Dapper;
using JSAGROSyncServices.Shared.DTOs.Allegro;
using Microsoft.Extensions.Options;
using RolmarAllegroProductsSyncService.Data;
using RolmarAllegroProductsSyncService.Models;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;
using RolmarAllegroProductsSyncService.Settings;
using System.Globalization;

namespace RolmarAllegroProductsSyncService.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        private readonly DapperContext _context;
        private readonly string _deliveryName;
        private readonly ILogger<OfferRepository> _logger;

        public OfferRepository(ILogger<OfferRepository> logger, DapperContext context, IOptions<AppSettings> options)
        {
            _logger = logger;
            _context = context;
            _deliveryName = options.Value.AllegroDeliveryName;
        }

        public async Task UpsertOffers(List<Offer> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any()) return;

            _logger.LogInformation("Starting upsert of {Count} offers", offers.Count);

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                const int batchSize = 1000;

                // 1️ Preload existing offer IDs in batches
                var allOfferIds = offers.Select(o => o.Id).ToList();
                var existingIds = new HashSet<string>();

                foreach (var batch in allOfferIds.Chunk(batchSize))
                {
                    var ids = await connection.QueryAsync<string>(
                        "SELECT Id FROM AllegroOffers WHERE Id IN @Ids",
                        new { Ids = batch },
                        transaction);
                    foreach (var id in ids)
                        existingIds.Add(id);
                }

                // 2️ Map AllegroOffer entities
                var allegroOffers = offers.Select(o =>
                {
                    decimal.TryParse(o.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var price);
                    int.TryParse(o.Category?.Id, out var categoryId);

                    return new AllegroOffer
                    {
                        Id = o.Id,
                        Name = o.Name ?? string.Empty,
                        ProductId = null,
                        CategoryId = categoryId,
                        Price = price,
                        Stock = o.Stock?.Available ?? 0,
                        WatchersCount = o.Stats?.WatchersCount ?? 0,
                        VisitsCount = o.Stats?.VisitsCount ?? 0,
                        Status = o.Publication?.Status ?? string.Empty,
                        DeliveryName = o.Delivery?.ShippingRates?.Name,
                        StartingAt = o.Publication?.StartingAt ?? new DateTime(1753, 1, 1),
                        ExternalId = o.External?.Id
                    };
                }).ToList();

                var newOffers = allegroOffers.Where(a => !existingIds.Contains(a.Id)).ToList();
                var updateOffers = allegroOffers.Where(a => existingIds.Contains(a.Id)).ToList();

                // 3️ Insert new offers in batches
                if (newOffers.Any())
                {
                    const string insertSql = @"
                        INSERT INTO AllegroOffers
                        (Id, Name, ProductId, CategoryId, Price, Stock, WatchersCount, VisitsCount, Status, DeliveryName, StartingAt, ExternalId)
                        VALUES
                        (@Id, @Name, @ProductId, @CategoryId, @Price, @Stock, @WatchersCount, @VisitsCount, @Status, @DeliveryName, @StartingAt, @ExternalId)";

                    foreach (var batch in newOffers.Chunk(batchSize))
                    {
                        await connection.ExecuteAsync(insertSql, batch, transaction);
                    }
                }

                // 4️ Update existing offers in batches
                if (updateOffers.Any())
                {
                    const string updateSql = @"
                        UPDATE AllegroOffers
                        SET Name = @Name,
                            CategoryId = @CategoryId,
                            Price = @Price,
                            Stock = @Stock,
                            WatchersCount = @WatchersCount,
                            VisitsCount = @VisitsCount,
                            Status = @Status,
                            DeliveryName = @DeliveryName,
                            StartingAt = @StartingAt
                        WHERE Id = @Id";

                    foreach (var batch in updateOffers.Chunk(batchSize))
                    {
                        await connection.ExecuteAsync(updateSql, batch, transaction);
                    }
                }

                transaction.Commit();
                _logger.LogInformation("Upsert of offers completed: {New} new, {Updated} updated", newOffers.Count, updateOffers.Count);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed upsert of offers");
                throw;
            }
        }

        public async Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            var sql = "SELECT * FROM AllegroOffers";
            return (await connection.QueryAsync<AllegroOffer>(sql)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteOffer(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            // First, get the product's CodeGaska
            var code = await connection.QueryFirstOrDefaultAsync<string>(
                @"SELECT Code FROM RolmarProducts WHERE Id = @ProductId",
                new { ProductId = productId }
            );

            if (code == null)
            {
                _logger.LogWarning("Product with Id {ProductId} not found. Cannot delete offer.", productId);
                return;
            }

            // Delete AllegroOffers where ExternalId = CodeGaska
            var sql = @"
                DELETE FROM AllegroOffers
                WHERE ExternalId = @Code";

            var affectedRows = await connection.ExecuteAsync(sql, new { Code = code });

            _logger.LogInformation("Deleted {Count} Allegro offer(s) for product {Code}.", affectedRows, code);
        }
    }
}