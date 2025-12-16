using RolmarAllegroProductsSyncService.Models;

namespace RolmarAllegroProductsSyncService.Services.Interfaces
{
    public interface IAllegroImageService
    {
        Task<List<OfferImage>> ImportImages(List<OfferImage> images, CancellationToken ct = default);
    }
}