using GaskaAllegroSync.Data;
using GaskaAllegroSync.Models.Product;
using GaskaAllegroSync.Repositories.Interfaces;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories
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
                .AsNoTracking()
                .Include(pi => pi.Product)
                .Include(pi => pi.Product.AllegroOffers)
                .Where(pi => pi.Product.Categories.Any()
                    && !pi.Product.Archived
                    && pi.Product.DefaultAllegroCategory != 0
                    && pi.Product.PriceGross > 1.00m
                    && pi.Product.InStock > 0
                    && !pi.Url.Contains("+")
                    && (string.IsNullOrEmpty(pi.AllegroUrl) || pi.AllegroExpirationDate <= DateTime.UtcNow)
                    && !pi.Product.AllegroOffers.Any())
                .ToListAsync(ct);
        }

        public async Task UpdateProductAllegroImages(List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)> images, CancellationToken ct)
        {
            if (images == null || !images.Any())
                return;

            var imageIds = images.Select(i => i.ImageId).ToList();

            // 1 Load all existing images in one query
            var existingImages = _context.ProductImages
                .Where(i => imageIds.Contains(i.Id))
                .ToList();

            var dict = existingImages.ToDictionary(i => i.Id);

            // 2 Map updates
            foreach (var update in images)
            {
                if (dict.TryGetValue(update.ImageId, out var img))
                {
                    img.AllegroUrl = update.Url;
                    img.AllegroLogoUrl = update.LogoUrl;
                    img.AllegroExpirationDate = update.ExpiresAt;
                }
            }

            // 3 Bulk update all images at once
            if (existingImages.Any())
                _context.BulkUpdate(existingImages);
        }
    }
}