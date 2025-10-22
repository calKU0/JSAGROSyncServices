using Dapper;
using AllegroGaskaProductsSyncService.Data;
using AllegroGaskaProductsSyncService.Models.Product;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using Serilog;

namespace AllegroGaskaProductsSyncService.Repositories
{
    public class ImageRepository : IImageRepository
    {
        private readonly DapperContext _context;
        private readonly ILogger<ImageRepository> _logger;

        public ImageRepository(DapperContext context, ILogger<ImageRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task DeleteImage(int productId)
        {
            const string sql = @"
            UPDATE ProductImages
            SET AllegroUrl = NULL,
                AllegroExpirationDate = GETDATE(),
                AllegroLogoUrl = NULL
            WHERE ProductId = @productId";

            using var connection = _context.CreateConnection();
            connection.Open();

            await connection.ExecuteAsync(sql, new { productId });
        }

        public async Task<List<ProductImage>> GetImagesForImport(CancellationToken ct)
        {
            const string sql = @"
                SELECT pi.*, p.Id, p.CodeGaska, p.Name, p.PriceGross, p.InStock, p.DefaultAllegroCategory
                FROM ProductImages pi
                INNER JOIN Products p ON pi.ProductId = p.Id
                LEFT JOIN AllegroOffers o on p.CodeGaska = o.ExternalId
                WHERE
                    (pi.AllegroUrl IS NULL OR pi.AllegroExpirationDate <= GETDATE())
                    AND p.Archived = 0
                    AND p.DefaultAllegroCategory != 0
                    AND p.PriceGross > 1
                    AND p.InStock > 0
                    AND o.Id is null
                    AND pi.Url NOT LIKE '%+%'";

            using var connection = _context.CreateConnection();
            connection.Open();
            var imageDict = new Dictionary<int, ProductImage>();

            var result = await connection.QueryAsync<ProductImage, Product, ProductImage>(
                sql,
                (pi, p) =>
                {
                    if (!imageDict.TryGetValue(pi.Id, out var image))
                    {
                        image = pi;
                        image.Product = p;
                        imageDict.Add(image.Id, image);
                    }
                    return image;
                },
                splitOn: "Id"
            );

            return result.Distinct().ToList();
        }

        public async Task UpdateProductAllegroImages(List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)> images, CancellationToken ct)
        {
            if (images == null || !images.Any())
                return;

            const string sql = @"
                UPDATE ProductImages
                SET AllegroUrl = @Url,
                    AllegroLogoUrl = @LogoUrl,
                    AllegroExpirationDate = @ExpiresAt
                WHERE Id = @ImageId";

            using var connection = _context.CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                // Batch update all images in one call
                await connection.ExecuteAsync(sql, images.Select(i => new
                {
                    i.ImageId,
                    i.Url,
                    i.LogoUrl,
                    i.ExpiresAt
                }), transaction);

                transaction.Commit();
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to update Allegro images in database.");
                throw;
            }
        }
    }
}