using JSAGROAllegroSync.DTOs.AllegroApi;
using JSAGROAllegroSync.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Repositories.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetAllOffers(CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);
    }
}