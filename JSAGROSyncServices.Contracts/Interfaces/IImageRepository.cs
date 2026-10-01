using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IImageRepository
    {
        Task<int> AddImageAsync(int productId, string url, CancellationToken ct);

        /// <summary>
        /// Adresy zdjęć produktu już wysłanych do Allegro. Dostawcy usuwają te wpisy, gdy zestaw
        /// plików na dysku się zmieni, więc ich obecność znaczy "te same zdjęcia, już wgrane"
        /// - nie ma po co wysyłać ich drugi raz.
        /// </summary>
        Task<List<AllegroImages>> GetProductImagesAsync(int productId, CancellationToken ct);

        Task MarkImagesAsConnectedAsync(int productId, CancellationToken ct);

        Task DeleteNotConnectedImages(int productId, CancellationToken ct);
        Task<int> DeleteProductImagesAsync(int productId, CancellationToken ct);
    }
}