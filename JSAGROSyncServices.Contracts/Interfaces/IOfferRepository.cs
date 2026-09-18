using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Contracts.Interfaces
{
    public interface IOfferRepository
    {
        Task UpsertOffers(List<Offer> offers, CancellationToken ct);

        Task<List<AllegroOffer>> GetOffersToUpdate(CancellationToken ct);

        /// <summary>
        /// Oferty, których produkty nie należą już do żadnej ze skonfigurowanych kategorii konta.
        /// Tylko z cenników obsługiwanych przez serwis - ofert wystawionych ręcznie nie ruszamy.
        /// </summary>
        Task<List<AllegroOffer>> GetOffersToEnd(CancellationToken ct);
        Task<List<AllegroOffer>> GetOffersWithoutDetails(CancellationToken ct);
        Task UpsertOfferDetails(List<AllegroOfferDetails.Root> offers, CancellationToken ct);

        /// <summary>Oznacza oferty jako majace pobrane szczegoly, niezaleznie od tego czy mialy wlasny opis.</summary>
        Task MarkDetailsFetched(IEnumerable<string> offerIds, CancellationToken ct);

        Task DeleteOffer(string offerId, CancellationToken ct);
        Task UpdateProductId(string offerId, string? value, CancellationToken ct);
    }
}