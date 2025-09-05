using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.Models.Product;
using JSAGROAllegroSync.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories
{
    public class ImageRepository : IImageRepository
    {
        private readonly MyDbContext _context;

        public ImageRepository(MyDbContext context)
        {
            _context = context;
        }

        public async Task<List<ProductImage>> GetImagesForImport(CancellationToken ct)
        {
            return await _context.ProductImages
                .Include(pi => pi.Product)
                .Include(pi => pi.Product.AllegroOffers)
                .Where(pi => pi.Product.Categories.Any()
                    && pi.Product.DefaultAllegroCategory != 0
                    && (string.IsNullOrEmpty(pi.AllegroUrl) || pi.AllegroExpirationDate <= DateTime.UtcNow)
                    && !pi.Product.AllegroOffers.Any())
                .ToListAsync(ct);
        }

        public async Task UpdateProductAllegroImages(List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)> images, CancellationToken ct)
        {
            if (images == null || !images.Any())
                return;

            const int batchSize = 100;

            var batches = images
                .Select((u, index) => new { u, index })
                .GroupBy(x => x.index / batchSize)
                .Select(g => g.Select(x => x.u).ToList());

            foreach (var batch in batches)
            {
                var ids = batch.Select(u => u.ImageId).ToList();

                var existingImages = await _context.ProductImages
                    .Where(i => ids.Contains(i.Id))
                    .ToListAsync(ct);

                var dict = existingImages.ToDictionary(i => i.Id);

                foreach (var update in batch)
                {
                    if (dict.TryGetValue(update.ImageId, out var img))
                    {
                        img.AllegroUrl = update.Url;
                        img.AllegroLogoUrl = update.LogoUrl;
                        img.AllegroExpirationDate = update.ExpiresAt;
                    }
                }

                await _context.SaveChangesAsync(ct);
            }
        }
    }
}