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

        /// <summary>Unieważnia adresy wgrane przez to konto - np. gdy Allegro odrzuciło adres zdjęcia.</summary>
        Task<int> DeleteProductImagesAsync(int productId, CancellationToken ct);

        /// <summary>
        /// Unieważnia adresy wgrane przez wszystkie konta. Tego wymaga zmiana zestawu plików
        /// u dostawcy: pliki są wspólne, a pobiera je tylko konto ciągnące dane od dostawcy,
        /// więc sprzątanie po jednym koncie zostawiłoby drugie ze starą galerią.
        /// </summary>
        Task<int> DeleteProductImagesForAllAccountsAsync(int productId, CancellationToken ct);
    }
}