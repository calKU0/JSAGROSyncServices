using AllegroGaskaProductsSyncService.Data;
using AllegroGaskaProductsSyncService.DTOs.AllegroApi;
using AllegroGaskaProductsSyncService.Models;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using AllegroGaskaProductsSyncService.Settings;
using Dapper;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace AllegroGaskaProductsSyncService.Repositories
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

        public async Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any()) return;

            _logger.LogInformation("Starting upsert of {Count} offer details", offers.Count);

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Preload existing offer IDs
                var offerIds = offers.Select(o => o.Id).ToList();
                var existingIds = (await connection.QueryAsync<string>(
                    "SELECT Id FROM AllegroOffers WHERE Id IN @Ids",
                    new { Ids = offerIds }, transaction)).ToHashSet();

                // Map AllegroOffer
                var allegroOffers = offers.Select(o =>
                {
                    decimal price = 0;
                    decimal.TryParse(o?.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out price);

                    int categoryId = 0;
                    int.TryParse(o?.Category?.Id, out categoryId);

                    return new AllegroOffer
                    {
                        Id = o.Id,
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

                // Split new and update
                var newOffers = allegroOffers.Where(a => !existingIds.Contains(a.Id)).ToList();
                var updateOffers = allegroOffers.Where(a => existingIds.Contains(a.Id)).ToList();

                // Insert new offers
                if (newOffers.Any())
                {
                    var insertSql = @"
                        INSERT INTO AllegroOffers
                        (Id, Name, CategoryId, Price, Stock, Status, DeliveryName, ExternalId, Weight, Images, StartingAt, HandlingTime, ResponsiblePerson, ResponsibleProducer)
                        VALUES
                        (@Id, @Name, @CategoryId, @Price, @Stock, @Status, @DeliveryName, @ExternalId, @Weight, @Images, @StartingAt, @HandlingTime, @ResponsiblePerson, @ResponsibleProducer)";
                    await connection.ExecuteAsync(insertSql, newOffers, transaction);
                }

                // Update existing offers
                if (updateOffers.Any())
                {
                    var updateSql = @"
                        UPDATE AllegroOffers
                        SET Name = @Name,
                            CategoryId = @CategoryId,
                            Price = @Price,
                            Stock = @Stock,
                            Status = @Status,
                            DeliveryName = @DeliveryName,
                            Weight = @Weight,
                            Images = @Images,
                            StartingAt = @StartingAt,
                            HandlingTime = @HandlingTime,
                            ResponsiblePerson = @ResponsiblePerson,
                            ResponsibleProducer = @ResponsibleProducer
                        WHERE Id = @Id";
                    await connection.ExecuteAsync(updateSql, updateOffers, transaction);
                }

                // ---- Descriptions ----
                var descriptions = new List<AllegroOfferDescription>();
                foreach (var o in offers)
                {
                    int sectionIndex = 1;
                    if (o.Description?.Sections != null)
                    {
                        foreach (var section in o.Description.Sections)
                        {
                            foreach (var item in section.Items)
                            {
                                descriptions.Add(new AllegroOfferDescription
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
                    var descSql = @"
                        INSERT INTO AllegroOfferDescriptions
                        (OfferId, Type, Content, SectionId)
                        VALUES (@OfferId, @Type, @Content, @SectionId)";
                    await connection.ExecuteAsync(descSql, descriptions, transaction);
                }

                // ---- Attributes ----
                var attributes = new List<AllegroOfferAttribute>();
                foreach (var o in offers)
                {
                    if (o.Parameters != null)
                    {
                        foreach (var param in o.Parameters)
                        {
                            attributes.Add(new AllegroOfferAttribute
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
                    var attrSql = @"
                        INSERT INTO AllegroOfferAttributes
                        (OfferId, AttributeId, Type, ValuesJson, ValuesIdsJson)
                        VALUES (@OfferId, @AttributeId, @Type, @ValuesJson, @ValuesIdsJson)";
                    await connection.ExecuteAsync(attrSql, attributes, transaction);
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
            var sql = "SELECT * FROM AllegroOffers";
            return (await connection.QueryAsync<AllegroOffer>(sql)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct)
        {
            using var connection = _context.CreateConnection();
            var sql = @"
                SELECT * FROM AllegroOffers o
                WHERE NOT EXISTS (SELECT 1 FROM AllegroOfferDescriptions d WHERE d.OfferId = o.Id)
                AND Status = 'ACTIVE'
                ORDER BY StartingAt DESC";
            return (await connection.QueryAsync<AllegroOffer>(sql)).ToList();
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            using var conn = _context.CreateConnection();
            conn.Open();

            const int SqlServerMaxParams = 2000;

            IEnumerable<List<T>> Batch<T>(IEnumerable<T> source, int batchSize)
            {
                var batch = new List<T>(batchSize);
                foreach (var item in source)
                {
                    batch.Add(item);
                    if (batch.Count >= batchSize)
                    {
                        yield return batch;
                        batch.Clear();
                    }
                }
                if (batch.Count > 0) yield return batch;
            }

            async Task<List<T>> QueryInBatchesAsync<T>(string sql, IEnumerable<int> ids)
            {
                var result = new List<T>();
                foreach (var batch in Batch(ids, SqlServerMaxParams))
                {
                    var batchResult = (await conn.QueryAsync<T>(sql, new { batch })).ToList();
                    result.AddRange(batchResult);
                }
                return result;
            }

            // 1️ Load active/ended offers with associated Product objects
            var offersWithProducts = (await conn.QueryAsync<AllegroOffer, Product, (AllegroOffer Offer, Product Product)>(@"
                SELECT
                    -- AllegroOffer columns
                    o.Id,
                    o.ExternalId,
                    o.Name,
                    o.CategoryId,
                    o.Price,
                    o.Stock,
                    o.WatchersCount,
                    o.VisitsCount,
                    o.Status,
                    o.DeliveryName,
                    o.StartingAt,
                    o.ExistsInErli,
                    o.Images AS OfferImages,
                    o.Weight AS OfferWeight,
                    o.HandlingTime,
                    o.ResponsibleProducer,
                    o.ResponsiblePerson,

                    -- Product columns
                    p.Id,
                    p.CodeGaska,
                    p.CodeCustomer,
                    p.Name,
                    p.Description,
                    p.Ean,
                    p.DeliveryType,
                    p.TechnicalDetails,
                    p.WeightNet,
                    p.WeightGross,
                    p.SupplierName,
                    p.SupplierLogo,
                    p.InStock,
                    p.Unit,
                    p.CurrencyPrice,
                    p.PriceNet,
                    p.PriceGross,
                    p.DefaultAllegroCategory,
                    p.Archived,
                    p.CreatedDate,
                    p.UpdatedDate
                FROM AllegroOffers o
                INNER JOIN Products p ON p.CodeGaska = o.ExternalId
                WHERE (o.Status = 'ACTIVE' OR o.Status = 'ENDED')
                  AND o.DeliveryName = @DeliveryName
                  AND EXISTS (SELECT 1 FROM ProductParameters pp WHERE pp.ProductId = p.Id)
                  AND EXISTS (SELECT 1 FROM ProductImages pi WHERE pi.ProductId = p.Id AND pi.AllegroUrl IS NOT NULL)
                  AND EXISTS (SELECT 1 FROM ProductCategories pc WHERE pc.ProductId = p.Id);",
                (offer, product) =>
                {
                    offer.Product = product;
                    return (offer, product);
                },
                new { DeliveryName = _deliveryName },
                splitOn: "Id"
            )).ToList();

            if (!offersWithProducts.Any())
                return new List<AllegroOffer>();

            // 2️ Extract product IDs for child entity queries
            var productIds = offersWithProducts.Select(op => op.Product.Id).ToArray();
            if (!productIds.Any())
                return offersWithProducts.Select(op => op.Offer).ToList();

            // 3️ Load child entities in batches
            var images = await QueryInBatchesAsync<ProductImage>(
                "SELECT ProductId, AllegroUrl, MAX(AllegroLogoUrl) AS AllegroLogoUrl FROM ProductImages WHERE ProductId IN @batch  AND AllegroUrl IS NOT NULL GROUP BY ProductId, AllegroUrl", productIds);

            var packages = await QueryInBatchesAsync<Package>(
                "SELECT * FROM Packages WHERE ProductId IN @batch;", productIds);

            var attributes = await QueryInBatchesAsync<ProductAttribute>(
                "SELECT * FROM ProductAttributes WHERE ProductId IN @batch;", productIds);

            var crossNumbers = await QueryInBatchesAsync<CrossNumber>(
                "SELECT * FROM CrossNumbers WHERE ProductId IN @batch;", productIds);

            var applications = await QueryInBatchesAsync<Application>(
                "SELECT * FROM Applications WHERE ProductId IN @batch;", productIds);

            var productParameters = new List<ProductParameter>();
            foreach (var batch in Batch(productIds, SqlServerMaxParams))
            {
                var batchParams = (await conn.QueryAsync<ProductParameter, CategoryParameter, ProductParameter>(@"
                    SELECT
                        pp.Id AS PpId, pp.ProductId, pp.CategoryParameterId, pp.Value, pp.IsForProduct,
                        cp.Id AS CpId, cp.ParameterId, cp.CategoryId, cp.Name, cp.Type, cp.Required,
                        cp.RequiredForProduct, cp.DescribesProduct, cp.CustomValuesEnabled, cp.AmbiguousValueId, cp.Min, cp.Max
                    FROM ProductParameters pp
                    INNER JOIN CategoryParameters cp ON cp.Id = pp.CategoryParameterId
                    WHERE pp.ProductId IN @batch;",
                    (pp, cp) =>
                    {
                        pp.CategoryParameter = cp;
                        return pp;
                    },
                    new { batch },
                    splitOn: "CpId"
                )).ToList();

                productParameters.AddRange(batchParams);
            }

            // 4️ Map child collections to each product inside the offer
            foreach (var op in offersWithProducts)
            {
                var product = op.Product;
                var pId = product.Id;

                product.Images = images.Where(i => i.ProductId == pId).ToList();
                product.Packages = packages.Where(pkg => pkg.ProductId == pId).ToList();
                product.Atributes = attributes.Where(a => a.ProductId == pId).ToList();
                product.CrossNumbers = crossNumbers.Where(cn => cn.ProductId == pId).ToList();
                product.Applications = applications.Where(app => app.ProductId == pId).ToList();
                product.Parameters = productParameters.Where(pp => pp.ProductId == pId).ToList();

                op.Offer.Product = product;
            }

            // 5️ Return only the offers
            return offersWithProducts.Select(op => op.Offer).ToList();
        }
    }
}