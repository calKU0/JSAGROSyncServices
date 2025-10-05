using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs.AllegroApi;
using GaskaAllegroSync.Helpers;
using GaskaAllegroSync.Models;
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
            if (offers == null || !offers.Any())
            {
                Log.Warning("No offers to upsert.");
                return;
            }

            Log.Information("Starting upsert of {Count} offers", offers.Count);
            var stopwatchTotal = Stopwatch.StartNew();

            // 1. Load all products for lookup
            var productCodes = offers.Select(o => o.External.Id).Distinct().ToList();
            var products = await _context.Products
                .AsNoTracking()
                .Where(p => productCodes.Contains(p.CodeGaska))
                .ToDictionaryAsync(p => p.CodeGaska, ct);

            // 2. Load existing offers for lookup
            var offerIds = offers.Select(o => o.Id).ToList();
            var existingOffers = await _context.AllegroOffers
                .Where(o => offerIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, ct);

            int inserts = 0, updates = 0;

            foreach (var offer in offers)
            {
                if (!products.TryGetValue(offer.External.Id, out var product))
                    continue;

                if (!existingOffers.TryGetValue(offer.Id, out var existing))
                {
                    existing = new AllegroOffer { Id = offer.Id };
                    _context.AllegroOffers.Add(existing);
                    inserts++;
                }
                else
                {
                    updates++;
                }

                existing.Name = offer.Name;
                existing.ProductId = product.Id;
                existing.CategoryId = Convert.ToInt32(offer.Category.Id);
                existing.Price = Decimal.Parse(offer.SellingMode.Price.Amount, CultureInfo.InvariantCulture);
                existing.Stock = offer.Stock.Available;
                existing.WatchersCount = offer.Stats.WatchersCount;
                existing.VisitsCount = offer.Stats.VisitsCount;
                existing.Status = offer.Publication.Status;
                existing.DeliveryName = offer.Delivery.ShippingRates.Name;
                existing.ExternalId = offer.External?.Id;
                existing.StartingAt = offer.Publication?.StartingAt ?? DateTime.MinValue;
            }

            await _context.SaveChangesAsync(ct);

            stopwatchTotal.Stop();
            Log.Information("Upserted offers completed: {Inserts} inserted, {Updates} updated in {Elapsed} ms",
                inserts, updates, stopwatchTotal.ElapsedMilliseconds);
        }

        public async Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct)
        {
            if (offers == null || !offers.Any())
            {
                Log.Warning("No offers to upsert.");
                return;
            }

            Log.Information("Starting upsert of {Count} offers", offers.Count);
            var stopwatchTotal = Stopwatch.StartNew();

            // 1. Load products for lookup
            var productCodes = offers.Select(o => o.External?.Id).Where(x => x != null).Distinct().ToList();
            var products = await _context.Products
                .AsNoTracking()
                .Where(p => productCodes.Contains(p.CodeGaska))
                .ToDictionaryAsync(p => p.CodeGaska, ct);

            // 2. Load existing offers
            var offerIds = offers.Select(o => o.Id).ToList();
            var existingOffers = await _context.AllegroOffers
                .Include(o => o.Attributes)
                .Include(o => o.Descriptions)
                .Where(o => offerIds.Contains(o.Id))
                .ToDictionaryAsync(o => o.Id, ct);

            int inserts = 0, updates = 0;

            foreach (var offer in offers)
            {
                products.TryGetValue(offer.External?.Id, out var product);

                AllegroOffer existing;
                if (!existingOffers.TryGetValue(offer.Id, out existing))
                {
                    existing = new AllegroOffer { Id = offer.Id };
                    _context.AllegroOffers.Add(existing);
                    inserts++;
                }
                else
                {
                    updates++;
                }

                // main offer fields
                existing.Name = offer.Name;
                existing.CategoryId = Convert.ToInt32(offer.Category.Id);
                existing.Price = Decimal.Parse(offer.SellingMode.Price.Amount, CultureInfo.InvariantCulture);
                existing.Stock = offer.Stock.Available;
                existing.Status = offer.Publication.Status;
                existing.DeliveryName = offer.Delivery.ShippingRates?.Id;
                existing.ExternalId = offer.External?.Id;
                existing.ProductId = product?.Id ?? null;
                existing.Weight = (decimal)product?.WeightGross;
                existing.Images = offer.Images != null
                    ? JsonConvert.SerializeObject(offer.Images)
                    : null;
                existing.StartingAt = offer.Publication?.StartingAt ?? DateTime.MinValue;

                // ---- Update Descriptions ----
                if (existing.Descriptions == null)
                    existing.Descriptions = new List<AllegroOfferDescription>();
                else
                {
                    _context.AllegroOfferDescriptions.RemoveRange(existing.Descriptions);
                    existing.Descriptions.Clear();
                }

                if (offer.Description?.Sections != null)
                {
                    foreach (var section in offer.Description.Sections)
                    {
                        foreach (var item in section.Items)
                        {
                            existing.Descriptions.Add(new AllegroOfferDescription
                            {
                                OfferId = existing.Id,
                                Type = item.Type,
                                Content = item.Type == "TEXT" ? item.Content : item.Url
                            });
                        }
                    }
                }

                // ---- Update Attributes ----
                if (existing.Attributes == null)
                    existing.Attributes = new List<AllegroOfferAttribute>();
                else
                {
                    _context.AllegroOfferAttributes.RemoveRange(existing.Attributes);
                    existing.Attributes.Clear();
                }

                if (offer.Parameters != null)
                {
                    foreach (var param in offer.Parameters)
                    {
                        // Find the first CategoryParameter row for this parameter
                        var def = _context.CategoryParameters
                            .FirstOrDefault(p => p.ParameterId.ToString() == param.Id);

                        string type = "PARAM"; // fallback if not found
                        if (def != null && !string.IsNullOrEmpty(def.Type))
                        {
                            type = def.Type;
                        }

                        existing.Attributes.Add(new AllegroOfferAttribute
                        {
                            OfferId = existing.Id,
                            AttributeId = param.Id,
                            Type = type, // type from CategoryParameters table
                            ValuesJson = JsonConvert.SerializeObject(param.Values ?? new List<string>()),
                            ValuesIdsJson = JsonConvert.SerializeObject(param.ValuesIds ?? new List<string>())
                        });
                    }

                    foreach (var param in offer.ProductSet.First().Product.Parameters)
                    {
                        // Find the first CategoryParameter row for this parameter
                        var def = _context.CategoryParameters
                            .FirstOrDefault(p => p.ParameterId.ToString() == param.Id);

                        string type = "PARAM"; // fallback if not found
                        if (def != null && !string.IsNullOrEmpty(def.Type))
                        {
                            type = def.Type;
                        }

                        existing.Attributes.Add(new AllegroOfferAttribute
                        {
                            OfferId = existing.Id,
                            AttributeId = param.Id,
                            Type = type, // type from CategoryParameters table
                            ValuesJson = JsonConvert.SerializeObject(param.Values ?? new List<string>()),
                            ValuesIdsJson = JsonConvert.SerializeObject(param.ValuesIds ?? new List<string>())
                        });
                    }
                }
            }

            await _context.SaveChangesAsync(ct);

            stopwatchTotal.Stop();
            Log.Information("Upserted offers completed: {Inserts} inserted, {Updates} updated in {Elapsed} ms",
                inserts, updates, stopwatchTotal.ElapsedMilliseconds);
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