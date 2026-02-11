using Allegro.JSAGRO2.Rolmar.ProductsService.Constants;
using Allegro.JSAGRO2.Rolmar.ProductsService.Settings;
using Dapper;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Data;
using Microsoft.Extensions.Options;
using System.Data;
using System.Globalization;

namespace Allegro.JSAGRO2.Rolmar.ProductsService.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        private readonly DapperContext _context;
        private readonly List<DeliverySettings> _deliveries;
        private readonly ILogger<OfferRepository> _logger;

        public OfferRepository(ILogger<OfferRepository> logger, DapperContext context, IOptions<AppSettings> options)
        {
            _logger = logger;
            _context = context;
            _deliveries = options.Value.Deliveries;
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
                // 1️ Map AllegroOffer entities
                var allegroOffers = offers.Select(o =>
                {
                    decimal.TryParse(o.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var price);
                    int.TryParse(o.Category?.Id, out var categoryId);

                    return new
                    {
                        Id = o.Id,
                        Account = ServiceConstants.Account,
                        Name = o.Name ?? string.Empty,
                        ProductId = (int?)null,
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

                foreach (var batch in allegroOffers.Chunk(batchSize))
                {
                    await connection.ExecuteAsync(
                        "AllegroOffers_Upsert",
                        batch,
                        transaction,
                        commandType: CommandType.StoredProcedure);
                }

                transaction.Commit();
                _logger.LogInformation("Upsert of offers completed: {Count} processed", allegroOffers.Count);
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
            connection.Open();
            return (await connection.QueryAsync<AllegroOffer>(
                "AllegroOffers_GetAll",
                new { Account = ServiceConstants.Account },
                commandType: CommandType.StoredProcedure)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            var deliveryNames = _deliveries?
                .Select(d => d.DeliveryName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            if (!deliveryNames.Any())
            {
                _logger.LogInformation("Brak skonfigurowanych dostaw — pomijam pobieranie ofert do aktualizacji.");
                return new List<AllegroOffer>();
            }

            using var connection = _context.CreateConnection();
            connection.Open();

            // Step 1: Get offers, images, and specs in one call
            var command = new CommandDefinition(
                "AllegroOffers_GetOffersToUpdate",
                new { DeliveryNames = string.Join(",", deliveryNames), IntegrationCompany = ServiceConstants.Company, Account = ServiceConstants.Account },
                commandTimeout: 900,
                cancellationToken: ct,
                commandType: CommandType.StoredProcedure);

            using var grid = await connection.QueryMultipleAsync(command);

            var offers = grid.Read<AllegroOffer, RolmarProduct, AllegroOffer>(
                (offer, product) =>
                {
                    offer.Product = product;
                    return offer;
                },
                splitOn: "Id").ToList();

            if (!offers.Any())
                return offers;

            offers = offers
                .GroupBy(o => o.Product.Id)
                .Select(g => g.OrderByDescending(o => o.StartingAt).First())
                .ToList();

            var allImages = grid.Read<AllegroImages>().ToList();
            var allSpecs = grid.Read<ProductSpecification>().ToList();

            // Step 2: Aggregate into product collections
            var imagesLookup = allImages.ToLookup(i => i.ProductId);
            var specsLookup = allSpecs.ToLookup(s => s.ProductId);

            foreach (var offer in offers)
            {
                var product = offer.Product;
                product.AllegroImages = imagesLookup[product.Id].ToList();
                product.Specifications = specsLookup[product.Id].ToList();
            }

            return offers;
        }

        public async Task DeleteOffer(int productId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            var code = await connection.ExecuteScalarAsync<string>(
                "AllegroOffers_DeleteByProductId",
                new { ProductId = productId },
                commandType: CommandType.StoredProcedure);

            if (string.IsNullOrWhiteSpace(code))
            {
                _logger.LogWarning("Product with Id {ProductId} not found. Cannot delete offer.", productId);
                return;
            }

            _logger.LogInformation("Deleted Allegro offer for product {Code}.", code);
        }

        public Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
