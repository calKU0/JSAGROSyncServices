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

            var table = new DataTable();
            table.Columns.Add("Id", typeof(string));
            table.Columns.Add("Account", typeof(int));
            table.Columns.Add("Name", typeof(string));
            var colProductId = new DataColumn("ProductId", typeof(int)) { AllowDBNull = true };
            table.Columns.Add(colProductId);
            table.Columns.Add("CategoryId", typeof(int));
            table.Columns.Add("Price", typeof(decimal));
            table.Columns.Add("Stock", typeof(int));
            table.Columns.Add("WatchersCount", typeof(int));
            table.Columns.Add("VisitsCount", typeof(int));
            table.Columns.Add("Status", typeof(string));
            var colDeliveryName = new DataColumn("DeliveryName", typeof(string)) { AllowDBNull = true };
            table.Columns.Add(colDeliveryName);
            table.Columns.Add("StartingAt", typeof(DateTime));
            var colExternalId = new DataColumn("ExternalId", typeof(string)) { AllowDBNull = true };
            table.Columns.Add(colExternalId);

            foreach (var o in offers)
            {
                decimal.TryParse(o.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out var price);
                int.TryParse(o.Category?.Id, out var categoryId);

                table.Rows.Add(
                    o.Id,
                    ServiceConstants.Account,
                    o.Name ?? string.Empty,
                    DBNull.Value,
                    categoryId,
                    price,
                    o.Stock?.Available ?? 0,
                    o.Stats?.WatchersCount ?? 0,
                    o.Stats?.VisitsCount ?? 0,
                    o.Publication?.Status ?? string.Empty,
                    o.Delivery?.ShippingRates?.Name ?? (object)DBNull.Value,
                    o.Publication?.StartingAt ?? new DateTime(1753, 1, 1),
                    o.External?.Id ?? (object)DBNull.Value
                );
            }

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // ONE database call for all rows
                await connection.ExecuteAsync(
                    "AllegroOffers_Upsert",
                    new { Offers = table.AsTableValuedParameter("dbo.AllegroOfferType") },
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900);

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
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

        public async Task DeleteOffer(string offerId, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteScalarAsync<string>(
                "AllegroOffers_Delete",
                new { OfferId = offerId },
                commandType: CommandType.StoredProcedure);


            _logger.LogInformation("Deleted Allegro offer {Id}.", offerId);
        }

        public Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task UpdateProductId(string offerId, string? value, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
