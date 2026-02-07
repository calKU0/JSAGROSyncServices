using JSAGROSyncServices.Shared.DTOs.Allegro;
using Allegro.JSAGRO.Rolmar.ProductsService.Models;

namespace Allegro.JSAGRO.Rolmar.ProductsService.Repositories.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);

        Task DeleteOffer(int productId, CancellationToken ct);
    }
}