using JSAGROSyncServices.Shared.DTOs.Allegro;
using RolmarAllegroProductsSyncService.Models;

namespace RolmarAllegroProductsSyncService.Repositories.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);

        Task DeleteOffer(int productId, CancellationToken ct);
    }
}