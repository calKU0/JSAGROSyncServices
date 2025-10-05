using GaskaAllegroSync.Models.Product;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories.Interfaces
{
    public interface IImageRepository
    {
        Task<List<ProductImage>> GetImagesForImport(CancellationToken ct);

        Task UpdateProductAllegroImages(List<(int ImageId, string Url, string LogoUrl, DateTime ExpiresAt)> images, CancellationToken ct);
    }
}