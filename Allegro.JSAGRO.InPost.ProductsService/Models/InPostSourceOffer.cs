using JSAGROSyncServices.Contracts.Models;

namespace Allegro.JSAGRO.InPost.ProductsService.Models
{
    /// <summary>
    /// Oferta Allegro z bazy razem ze wszystkim, z czego budujemy ofertę InPost: produkt dostawcy
    /// (cena zakupu, stan, EAN, wymiary), opis, parametry oferty i produktu, kategorie dostawcy
    /// oraz stan oferty po stronie InPost, jeśli już ją wysłaliśmy.
    /// </summary>
    public class InPostSourceOffer
    {
        public string Id { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DeliveryName { get; set; } = string.Empty;
        public string? AllegroProductId { get; set; }
        public string? Images { get; set; }
        public decimal Weight { get; set; }
        public string? HandlingTime { get; set; }
        public string? ResponsibleProducer { get; set; }
        public string? ResponsiblePerson { get; set; }
        public int VisitsCount { get; set; }

        public RolmarProduct Product { get; set; } = new();

        public List<AllegroOfferDescription> Descriptions { get; set; } = new();

        /// <summary>Parametry oferty Allegro: nazwa → wartości.</summary>
        public List<NamedValues> OfferParameters { get; set; } = new();

        /// <summary>Ścieżki kategorii dostawcy, np. "WARYŃSKI ORIGIN>Hydraulika".</summary>
        public List<string> SupplierCategoryPaths { get; set; } = new();

        public Guid? InPostOfferId { get; set; }
        public Guid? LastCommandId { get; set; }
        public string? InPostStatus { get; set; }
        public decimal? InPostPrice { get; set; }
        public int? InPostStock { get; set; }
    }

    public class NamedValues
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Values { get; set; } = new();
    }

    /// <summary>Kategoria Allegro: id, nazwa i id rodzica (null = korzeń).</summary>
    public class AllegroCategoryNode
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int? ParentId { get; set; }
    }

    /// <summary>Oferta już wysłana do InPost (stan z tabeli InPostOffers).</summary>
    public class TrackedInPostOffer
    {
        public string AllegroOfferId { get; set; } = string.Empty;
        public string ExternalId { get; set; } = string.Empty;
        public Guid? InPostOfferId { get; set; }
        public Guid? LastCommandId { get; set; }
        public string? Status { get; set; }
        public decimal? Price { get; set; }
        public int? Stock { get; set; }
        public string? ContentHash { get; set; }

        /// <summary>Cena / stan wysłane zbiorczo i czekające na wynik komendy InPost.</summary>
        public decimal? PendingPrice { get; set; }
        public Guid? PriceCommandId { get; set; }
        public int? PendingStock { get; set; }
        public Guid? StockCommandId { get; set; }
    }
}
