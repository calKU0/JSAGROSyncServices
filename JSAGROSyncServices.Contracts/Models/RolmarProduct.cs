using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Contracts.Models
{
    public class RolmarProduct
    {
        public int Id { get; set; }
        public string? AllegroId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? CustomerCode { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Ean { get; set; }
        public float Weight { get; set; }
        public string? Fits { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierLogo { get; set; }
        public string? Substitutes { get; set; }
        public float InStock { get; set; }
        public string Unit { get; set; } = string.Empty;
        public string? CurrencyPrice { get; set; }
        public decimal PriceNet { get; set; }
        public decimal PriceGross { get; set; }

        /// <summary>
        /// Ostatnia zaakceptowana cena zakupu brutto. Przesuwa się razem z ceną, dopóki zmiana
        /// mieści się w limicie spadku, więc stopniowe obniżki nie blokują aktualizacji cen.
        /// </summary>
        public decimal? AcceptedPriceGross { get; set; }

        /// <summary>Kiedy wykryto zbyt duży spadek ceny zakupu. <c>null</c> = cena bez zastrzeżeń.</summary>
        public DateTime? PriceDropDetectedAt { get; set; }
        public int DefaultAllegroCategory { get; set; }
        public decimal Package { get; set; }
        public bool BuildCompatibilitySet { get; set; } = true;
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
        public IntegrationCompany IntegrationCompany { get; set; }
        public int IntegrationId { get; set; }
        public int DeliveryType { get; set; }
        public List<ProductPackage> Packages { get; set; } = new();
        public List<ProductApplication> Applications { get; set; } = new();
        public List<ProductSpecification> Specifications { get; set; } = new();
        public List<ProductParameter> Parameters { get; set; } = new();
        /// <summary>
        /// Klucze kategorii-lisci dostawcy (Gąska: id z API, Rolmar: znormalizowana ścieżka).
        /// <c>null</c> oznacza "nie znamy kategorii w tej operacji" - zapis NIE rusza wtedy
        /// przypisań w bazie. Pusta lista oznacza "produkt nie ma kategorii" i czyści przypisania.
        /// Lista produktów Gąski nie niesie kategorii, więc zostawia je <c>null</c>.
        /// </summary>
        public List<string>? CategoryKeys { get; set; }
        public List<AllegroImages> AllegroImages { get; set; } = new();
    }
}