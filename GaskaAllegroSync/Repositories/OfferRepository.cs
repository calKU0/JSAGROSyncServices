using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs.AllegroApi;
using GaskaAllegroSync.Helpers;
using GaskaAllegroSync.Models;
using GaskaAllegroSync.Models.Product;
using GaskaAllegroSync.Repositories.Interfaces;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Z.EntityFramework.Extensions;

namespace GaskaAllegroSync.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        private readonly MyDbContext _context;
        private readonly string _deliveryName = AppSettingsLoader.LoadAppSettings().AllegroDeliveryName;

        public OfferRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task UpsertOffers(List<Offer> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any()) return;

            Log.Information("Starting bulk upsert of {Count} offers", offers.Count);
            var stopwatchTotal = Stopwatch.StartNew();

            // Preload products
            var productCodes = offers.Select(o => o.External?.Id)
                .Where(x => x != null)
                .Distinct()
                .ToList();

            var products = _context.Products
                .Where(p => productCodes.Contains(p.CodeGaska))
                .ToDictionary(p => p.CodeGaska);

            // Preload existing offers
            var offerIds = offers.Select(o => o.Id).ToList();
            var existingOffersDict = _context.AllegroOffers
                .Where(o => offerIds.Contains(o.Id))
                .ToDictionary(o => o.Id);

            // Map all offers
            var allegroOffers = offers.Select(o =>
            {
                products.TryGetValue(o.External?.Id, out var product);

                return new AllegroOffer
                {
                    Id = o.Id,
                    Name = o.Name ?? string.Empty,
                    ProductId = product?.Id,
                    CategoryId = o.Category != null ? Convert.ToInt32(o.Category.Id) : 0,
                    Price = o.SellingMode?.Price != null
                        ? Decimal.Parse(o.SellingMode.Price.Amount ?? "0", CultureInfo.InvariantCulture)
                        : 0m,
                    Stock = o.Stock?.Available ?? 0,
                    WatchersCount = o.Stats?.WatchersCount ?? 0,
                    VisitsCount = o.Stats?.VisitsCount ?? 0,
                    Status = o.Publication?.Status ?? string.Empty,
                    DeliveryName = o.Delivery?.ShippingRates?.Name,
                    HandlingTime = o.Delivery?.HandlingTime,
                    ExternalId = o.External?.Id,
                    StartingAt = o.Publication?.StartingAt ?? DateTime.MinValue,
                };
            }).ToList();

            // Split into new and existing
            var newOffers = allegroOffers.Where(a => !existingOffersDict.ContainsKey(a.Id)).ToList();
            var updateOffers = allegroOffers.Where(a => existingOffersDict.ContainsKey(a.Id)).ToList();

            if (newOffers.Any())
                _context.BulkInsert(newOffers);

            if (updateOffers.Any())
            {
                _context.BulkUpdate(updateOffers, options =>
                {
                    options.IgnoreOnUpdateExpression = c => new
                    {
                        c.Images,
                        c.Descriptions,
                        c.Attributes,
                        c.ExistsInErli,
                        c.ResponsiblePerson,
                        c.ResponsibleProducer
                    };
                });
            }

            stopwatchTotal.Stop();
            Log.Information("Bulk upsert of offers completed in {Elapsed} ms", stopwatchTotal.ElapsedMilliseconds);
        }

        public async Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any()) return;

            Log.Information("Starting bulk upsert of {Count} offer details", offers.Count);
            var stopwatchTotal = Stopwatch.StartNew();

            // Preload products and category parameters
            var productCodes = offers.Select(o => o.External?.Id)
                .Where(x => x != null)
                .Distinct()
                .ToList();

            var products = _context.Products
                .Where(p => productCodes.Contains(p.CodeGaska))
                .GroupBy(p => p.CodeGaska)
                .ToDictionary(g => g.Key, g => g.First());

            var categoryParams = _context.CategoryParameters
                .GroupBy(p => p.ParameterId.ToString())
                .ToDictionary(g => g.Key, g => g.First());

            // Collect all offer IDs
            var offerIds = offers.Select(o => o.Id).ToList();

            // Then safely create the dictionary
            var existingOffersDict = _context.AllegroOffers
                .Include("Attributes")
                .Include("Descriptions")
                .Where(o => offerIds.Contains(o.Id))
                .GroupBy(o => o.Id)
                .ToDictionary(g => g.Key, g => g.First());

            // Map AllegroOffer entities
            var allegroOffers = offers.Select(o =>
            {
                products.TryGetValue(o.External?.Id, out var product);

                // safely parse price
                decimal price = 0;
                if (!decimal.TryParse(o?.SellingMode?.Price?.Amount, NumberStyles.Any, CultureInfo.InvariantCulture, out price))
                    price = 0;

                int categoryId = 0;
                if (!int.TryParse(o?.Category?.Id, out categoryId))
                    categoryId = 0;

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
                    ProductId = product?.Id,
                    Weight = (decimal?)product?.WeightGross ?? 0m,
                    Images = o?.Images != null ? System.Text.Json.JsonSerializer.Serialize(o.Images) : null,
                    StartingAt = o?.Publication?.StartingAt ?? DateTime.MinValue,
                    HandlingTime = o?.Delivery?.HandlingTime,
                    ResponsiblePerson = o?.ProductSet?.FirstOrDefault()?.ResponsiblePerson?.Id ?? string.Empty,
                    ResponsibleProducer = o?.ProductSet?.FirstOrDefault()?.ResponsibleProducer?.Id ?? string.Empty
                };
            }).ToList();

            // Split parent offers
            var updateOffers = allegroOffers.Where(a => existingOffersDict.ContainsKey(a.Id)).ToList();
            if (updateOffers.Any())
            {
                _context.BulkUpdate(updateOffers, options =>
                {
                    options.IgnoreOnUpdateExpression = c => new
                    {
                        c.Id,
                        c.Name,
                        c.CategoryId,
                        c.Price,
                        c.Stock,
                        c.Status,
                        c.DeliveryName,
                        c.ExistsInErli,
                        c.ExternalId,
                        c.ProductId,
                        c.WatchersCount,
                        c.VisitsCount,
                    };
                });
            }

            // ---- Child Descriptions ----
            var allegroDescriptions = new List<AllegroOfferDescription>();
            foreach (var o in offers)
            {
                int sectionIndex = 1;
                if (o.Description?.Sections != null)
                {
                    foreach (var section in o.Description.Sections)
                    {
                        foreach (var item in section.Items)
                        {
                            allegroDescriptions.Add(new AllegroOfferDescription
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

            if (allegroDescriptions.Any()) _context.BulkInsert(allegroDescriptions);

            // ---- Child Attributes ----
            var allegroAttributes = new List<AllegroOfferAttribute>();
            foreach (var o in offers)
            {
                if (o.Parameters != null)
                {
                    foreach (var param in o.Parameters)
                    {
                        string type = "PARAM";
                        if (categoryParams.TryGetValue(param.Id, out var def) && !string.IsNullOrEmpty(def.Type))
                            type = def.Type;

                        allegroAttributes.Add(new AllegroOfferAttribute
                        {
                            OfferId = o.Id,
                            AttributeId = param.Id,
                            Type = type,
                            ValuesJson = System.Text.Json.JsonSerializer.Serialize(param.Values ?? new List<string>()),
                            ValuesIdsJson = System.Text.Json.JsonSerializer.Serialize(param.ValuesIds ?? new List<string>())
                        });
                    }
                }

                var productParams = o.ProductSet?.FirstOrDefault()?.Product?.Parameters;
                if (productParams != null)
                {
                    foreach (var param in productParams)
                    {
                        string type = "PARAM";
                        if (categoryParams.TryGetValue(param.Id, out var def) && !string.IsNullOrEmpty(def.Type))
                            type = def.Type;

                        allegroAttributes.Add(new AllegroOfferAttribute
                        {
                            OfferId = o.Id,
                            AttributeId = param.Id,
                            Type = type,
                            ValuesJson = System.Text.Json.JsonSerializer.Serialize(param.Values ?? new List<string>()),
                            ValuesIdsJson = System.Text.Json.JsonSerializer.Serialize(param.ValuesIds ?? new List<string>())
                        });
                    }
                }
            }

            if (allegroAttributes.Any()) _context.BulkInsert(allegroAttributes);

            stopwatchTotal.Stop();
            Log.Information("Bulk upsert of offer details completed in {Elapsed} ms", stopwatchTotal.ElapsedMilliseconds);
        }

        public async Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct)
        {
            return await _context.AllegroOffers.ToListAsync(ct);
        }

        public async Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct)
        {
            var offers = await _context.AllegroOffers
                .AsNoTracking()
                .Where(o => !o.Descriptions.Any())
                .OrderByDescending(o => o.StartingAt)
                .ToListAsync(ct);

            var distinctOffers = offers
                .GroupBy(o => o.ExternalId)
                .Select(g => g.First())
                .OrderByDescending(o => o.StartingAt)
                .ToList();

            return distinctOffers;
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            const int batchSize = 500;
            var result = new List<AllegroOffer>();

            // 1. Get only latest offer IDs
            var latestOfferIds = await _context.AllegroOffers
                .AsNoTracking()
                .Where(o => (o.Status == "ACTIVE" || o.Status == "ENDED")
                         && o.DeliveryName == _deliveryName)
                .GroupBy(o => o.ExternalId)
                .Select(g => g.Max(o => o.Id))
                .ToListAsync(ct);

            // 2. Process offers in batches
            for (int i = 0; i < latestOfferIds.Count; i += batchSize)
            {
                var batchIds = latestOfferIds.Skip(i).Take(batchSize).ToList();

                var batch = await _context.AllegroOffers
                    .Where(o => batchIds.Contains(o.Id))
                    .AsNoTracking()
                    .Include(o => o.Product)
                    .Include(o => o.Product.Parameters)
                    .Include(o => o.Product.Images)
                    .Include(o => o.Product.Categories)
                    .Where(o => o.Product.Parameters.Any()
                             && o.Product.Categories.Any()
                             && o.Product.Images.Any(im => !string.IsNullOrEmpty(im.AllegroUrl)))
                    .ToListAsync(ct);

                result.AddRange(batch);
            }

            return result;
        }
    }
}