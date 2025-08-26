using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Models;
using JSAGROAllegroSync.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.Entity;
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

        public OfferRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task UpsertOffers(List<Offer> offers, CancellationToken ct)
        {
            var products = _context.Products.ToDictionary(p => p.CodeGaska);
            var existingOffers = _context.AllegroOffers.ToDictionary(o => o.Id);

            foreach (var offer in offers)
            {
                if (!products.TryGetValue(offer.External.Id, out var product))
                    continue;

                if (!existingOffers.TryGetValue(offer.Id, out var existing))
                {
                    existing = new AllegroOffer { Id = offer.Id };
                    _context.AllegroOffers.Add(existing);
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
                existing.StartingAt = offer.Publication.StartingAt;
            }

            await _context.SaveChangesAsync(ct);
        }

        public async Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct)
        {
            return await _context.AllegroOffers.ToListAsync(ct);
        }

        public async Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct)
        {
            return await _context.AllegroOffers
                .Include(o => o.Product)
                .Include(o => o.Product.Applications)
                .Include(o => o.Product.Atributes)
                .Include(o => o.Product.Parameters)
                .Include(o => o.Product.Images)
                .Include(o => o.Product.Packages)
                .Include(o => o.Product.CrossNumbers)
                .Where(o => (o.Status == "ACTIVE" || o.Status == "INACTIVE" || o.Status == "ENDED")
                && o.DeliveryName == "JAG API")
                .ToListAsync(ct);
        }
    }
}