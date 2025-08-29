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
                    && ((string.IsNullOrEmpty(pi.AllegroUrl) || pi.AllegroExpirationDate <= DateTime.UtcNow)
                        && pi.Product.AllegroOffers.OrderByDescending(o => o.Id).FirstOrDefault().Status != "ACTIVE"))
                .ToListAsync(ct);
        }

        public async Task<bool> UpdateProductAllegroImage(int imageId, string imageUrl, string logoUrl, DateTime expiresAt, CancellationToken ct)
        {
            var image = await _context.ProductImages.FirstOrDefaultAsync(i => i.Id == imageId, ct);
            if (image == null) return false;

            image.AllegroUrl = imageUrl;
            image.AllegroLogoUrl = logoUrl;
            image.AllegroExpirationDate = expiresAt;

            return await _context.SaveChangesAsync(ct) > 0;
        }
    }
}