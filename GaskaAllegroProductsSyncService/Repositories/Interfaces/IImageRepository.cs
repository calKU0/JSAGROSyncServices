using AllegroGaskaProductsSyncService.Models.Product;

namespace AllegroGaskaProductsSyncService.Repositories.Interfaces
{
    public interface IImageRepository
    {
        Task<List<ProductImage>> GetImagesForImport(CancellationToken ct);

        Task UpdateProductAllegroImages(List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)> images, CancellationToken ct);
        Task DeleteImage(int imageId);
    }
}