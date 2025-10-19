using GaskaAllegroSyncService.DTOs.AllegroApi;
using GaskaAllegroSyncService.Models;

namespace GaskaAllegroSyncService.Repositories.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);
    }
}