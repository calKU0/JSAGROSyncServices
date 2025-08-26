using JSAGROAllegroSync.Models.Product;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories.Interfaces
{
    public interface IImageRepository
    {
        Task<List<ProductImage>> GetImagesForImport(CancellationToken ct);

        Task<bool> UpdateProductAllegroImage(int imageId, string imageUrl, DateTime expiresAt, CancellationToken ct);
    }
}