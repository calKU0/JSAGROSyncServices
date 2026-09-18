using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Budowanie żądań do Allegro. Reguły cen, opisów i parametrów różnią się między dostawcami,
    /// więc każdy dostawca ma własną implementację, a reszta serwisu jest wspólna.
    /// </summary>
    public interface IOfferFactory
    {
        /// <summary>Dane potrzebne w całym cyklu (np. drzewo kategorii Allegro). Wywoływane raz przed pętlą ofert.</summary>
        Task PrepareAsync(CancellationToken ct);

        ProductOfferRequest BuildOffer(RolmarProduct product);

        ProductOfferRequest PatchOffer(AllegroOffer offer, bool keepCurrentPrice);

        decimal CalculatePrice(RolmarProduct product);

        /// <summary>Patch kończący ofertę - wysyłamy wyłącznie status, bez żadnych innych danych.</summary>
        ProductOfferRequest EndOffer() => new ProductOfferRequest
        {
            Publication = new Publication
            {
                Status = "ENDED"
            }
        };
    }
}
