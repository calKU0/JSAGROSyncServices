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

        /// <summary>
        /// Czy oferty tego dostawcy wolno wystawiać wyłącznie po podpięciu pod istniejący produkt
        /// z katalogu Allegro. Przy <c>true</c> nie proponujemy Allegro nowego produktu: oferta bez
        /// dopasowanego produktu czeka na krok wyszukiwania, zamiast lecieć jako propozycja, której
        /// Allegro i tak nie przyjmie bez kompletu parametrów produktowych.
        /// </summary>
        bool RequiresCatalogProduct => false;

        /// <summary>
        /// Czy oferty tego dostawcy mają własną galerię. Przy <c>false</c> nie pobieramy ani nie
        /// wysyłamy zdjęć: galerię pokazuje produkt z katalogu Allegro, pod który oferta jest
        /// podpięta, a wysłanie własnych zdjęć tylko by ją nadpisało.
        /// </summary>
        bool UsesOwnImages => true;

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
