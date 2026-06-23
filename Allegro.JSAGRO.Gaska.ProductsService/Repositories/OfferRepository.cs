using Allegro.JSAGRO.Gaska.ProductsService.Constants;
using Allegro.JSAGRO.Gaska.ProductsService.Settings;
using Dapper;
using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Infrastructure.Data;
using Microsoft.Extensions.Options;
using System.Data;
using System.Globalization;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        private readonly DapperContext _context;
        private readonly AppSettings _appSettings;
        private readonly ILogger<OfferRepository> _logger;

        public OfferRepository(ILogger<OfferRepository> logger, DapperContext context, IOptions<AppSettings> options)
        {
            _logger = logger;
            _context = context;
            _appSettings = options.Value;
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

        public async Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any()) return;

            _logger.LogInformation("Starting upsert of {Count} offer details", offers.Count);

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                var allegroOffers = offers.Select(o =>
                {
                    decimal price = 0;
                    decimal.TryParse(o?.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    int categoryId = 0;
                    int.TryParse(o?.Category?.Id, out categoryId);

                    return new
                    {
                        Id = o.Id,
                        Account = ServiceConstants.Account,
                        Name = o.Name ?? string.Empty,
                        CategoryId = categoryId,
                        Price = price,
                        Stock = o?.Stock?.Available ?? 0,
                        Status = o?.Publication?.Status ?? "UNKNOWN",
                        DeliveryName = o?.Delivery?.ShippingRates?.Id,
                        ExternalId = o?.External?.Id,
                        Weight = 0,
                        Images = o?.Images != null ? System.Text.Json.JsonSerializer.Serialize(o.Images) : null,
                        StartingAt = o?.Publication?.StartingAt ?? new DateTime(1753, 1, 1),
                        HandlingTime = o?.Delivery?.HandlingTime,
                        ResponsiblePerson = o?.ProductSet?.FirstOrDefault()?.ResponsiblePerson?.Id ?? string.Empty,
                        ResponsibleProducer = o?.ProductSet?.FirstOrDefault()?.ResponsibleProducer?.Id ?? string.Empty
                    };
                }).ToList();

                await connection.ExecuteAsync(
                    "AllegroOffers_UpsertDetails",
                    allegroOffers,
                    transaction,
                    commandType: CommandType.StoredProcedure,
                    commandTimeout: 900);

                var productIdUpdates = offers
                    .Select(o => new
                    {
                        OfferId = o.Id,
                        ProductId = o?.ProductSet?.FirstOrDefault()?.Product?.Id,
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.OfferId) && !string.IsNullOrWhiteSpace(x.ProductId))
                    .GroupBy(x => new { x.OfferId, x.ProductId })
                    .Select(g => new
                    {
                        g.Key.OfferId,
                        g.Key.ProductId,
                        Account = ServiceConstants.Account,
                    })
                    .ToList();

                if (productIdUpdates.Any())
                {
                    await connection.ExecuteAsync(
                        "AllegroOffers_UpsertAllegroId",
                        productIdUpdates,
                        transaction,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 900);
                }

                // ---- Descriptions ----
                var descriptions = new List<object>();
                foreach (var o in offers)
                {
                    int sectionIndex = 1;
                    if (o.Description?.Sections != null)
                    {
                        foreach (var section in o.Description.Sections)
                        {
                            foreach (var item in section.Items)
                            {
                                descriptions.Add(new
                                {
                                    OfferId = o.Id,
                                    Type = item.Type,
                                    Content = item.Type == "TEXT" ? item.Content : item.Url,
                                    SectionId = sectionIndex
                                });
                            }
                            sectionIndex++;
                        }
                    }
                }

                if (descriptions.Any())
                {
                    await connection.ExecuteAsync(
                        "AllegroOfferDescriptions_Insert",
                        descriptions,
                        transaction,
                        commandType: CommandType.StoredProcedure,
                        commandTimeout: 900);
                }

                // ---- Attributes ----
                var attributes = new List<object>();
                foreach (var o in offers)
                {
                    if (o.Parameters != null)
                    {
                        foreach (var param in o.Parameters)
                        {
                            attributes.Add(new
                            {
                                OfferId = o.Id,
                                AttributeId = param.Id,
                                Type = param.ValuesIds?.Any() == true ? "dictionary" : "string",
                                ValuesJson = System.Text.Json.JsonSerializer.Serialize(param.Values ?? new List<string>()),
                                ValuesIdsJson = System.Text.Json.JsonSerializer.Serialize(param.ValuesIds ?? new List<string>())
                            });
                        }
                    }
                }

                if (attributes.Any())
                {
                    await connection.ExecuteAsync(
                        "AllegroOfferAttributes_Insert",
                        attributes,
                        transaction,
                        commandType: CommandType.StoredProcedure);
                }


                transaction.Commit();
                _logger.LogInformation("Upsert of offer details completed");
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed upsert of offer details");
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
                commandType: CommandType.StoredProcedure,
                commandTimeout: 900)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            return (await connection.QueryAsync<AllegroOffer>(
                "AllegroOffers_GetWithoutDetails",
                new { Account = ServiceConstants.Account },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 900)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            var deliveryNames = _appSettings.Deliveries?
                .Select(d => d.DeliveryName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            deliveryNames.AddRange(_appSettings.DeliveriesWithoutPriceUpdate ?? new List<string>());

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
            var allApplications = grid.Read<ProductApplication>().ToList();
            var allPackages = grid.Read<ProductPackage>().ToList();
            var allParameters = grid.Read<ProductParameter>().ToList();

            // Step 2: Aggregate into product collections
            var imagesLookup = allImages.ToLookup(i => i.ProductId);
            var specsLookup = allSpecs.ToLookup(s => s.ProductId);
            var applicationsLookup = allApplications.ToLookup(a => a.ProductId);
            var packagesLookup = allPackages.ToLookup(p => p.ProductId);
            var parametersLookup = allParameters.ToLookup(p => p.ProductId);

            foreach (var offer in offers)
            {
                var product = offer.Product;
                product.AllegroImages = imagesLookup[product.Id].ToList();
                product.Specifications = specsLookup[product.Id].ToList();
                product.Applications = applicationsLookup[product.Id].ToList();
                product.Packages = packagesLookup[product.Id].ToList();
                product.Parameters = parametersLookup[product.Id].ToList();
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

        private static bool TryParseDecimal(string input, out decimal result)
        {
            // akceptuj zarówno kropkę, jak i przecinek przy wprowadzaniu; zapisuj zawsze w formacie InvariantCulture
            return decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ||
            decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
        }

        public async Task UpdateProductId(string offerId, string? value, CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(
                "AllegroOffers_UpsertAllegroId",
                new { OfferId = offerId, ProductId = value, Account = ServiceConstants.Account },
                commandType: CommandType.StoredProcedure,
                commandTimeout: 900);
        }
    }
}