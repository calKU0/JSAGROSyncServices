using GaskaAllegroSync.DTOs.AllegroApi;
using GaskaAllegroSync.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync.Repositories.Interfaces
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