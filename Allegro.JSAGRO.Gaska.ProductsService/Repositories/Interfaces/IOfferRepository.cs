using Allegro.JSAGRO.Gaska.ProductsService.Models;
using JSAGROSyncServices.Shared.DTOs.Allegro;

namespace Allegro.JSAGRO.Gaska.ProductsService.Repositories.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);

        Task DeleteOffer(int productId, CancellationToken ct);
    }
}