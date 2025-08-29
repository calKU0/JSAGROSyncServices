using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.DTOs.Settings;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Repositories.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
{
    public class OfferRepository : IOfferRepository
    {
        private readonly MyDbContext _context;
        private readonly string _deliveryName = Helpers.AppSettingsLoader.LoadAppSettings().AllegroDeliveryName;

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

        public async Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct)
        {
            return await _context.AllegroOffers.ToListAsync(ct);
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            var lastOffers = await _context.AllegroOffers
                .Where(o => (o.Status == "ACTIVE" || o.Status == "ENDED") && o.DeliveryName == _deliveryName)
                .GroupBy(o => o.ExternalId)
                .Select(g => g.OrderByDescending(o => o.Id).FirstOrDefault())
                .ToListAsync(ct);

            var offerIds = lastOffers.Select(o => o.Id).ToList();

            var result = await _context.AllegroOffers
                .Where(o => offerIds.Contains(o.Id))
                .Include(o => o.Product)
                .Include(o => o.Product.Applications)
                .Include(o => o.Product.Atributes)
                .Include(o => o.Product.Parameters)
                .Include(o => o.Product.Images)
                .Include(o => o.Product.Packages)
                .Include(o => o.Product.CrossNumbers)
                .Where(o => o.Product.Parameters.Any() && o.Product.Categories.Any())
                .ToListAsync(ct);

            return result;
        }
    }
}