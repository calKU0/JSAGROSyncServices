using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.DTOs.InterCars
{
    /// <summary>
    /// Zapytanie o wycenę (POST /ic/inventory/quote). Maksymalnie 100 pozycji.
    /// </summary>
    public sealed class InterCarsQuoteRequest
    {
        [JsonPropertyName("lines")]
        public List<InterCarsQuoteLine> Lines { get; set; } = new();
    }

    public sealed class InterCarsQuoteLine
    {
        [JsonPropertyName("sku")]
        public string Sku { get; set; } = string.Empty;

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; } = 1;
    }

    /// <summary>
    /// Wycena jednej pozycji: cena, blokada zwrotu, EAN-y i dostępność w poszczególnych magazynach.
    /// W odpowiedzi są wyłącznie towary, które da się kupić - brak pozycji oznacza zerowy stan.
    /// </summary>
    public sealed class InterCarsQuoteItem
    {
        [JsonPropertyName("sku")]
        public string? Sku { get; set; }

        [JsonPropertyName("price")]
        public InterCarsPrice? Price { get; set; }

        [JsonPropertyName("index")]
        public string? Index { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("blockedReturn")]
        public bool BlockedReturn { get; set; }

        [JsonPropertyName("eans")]
        public List<string>? Eans { get; set; }

        /// <summary>Dostępność w rozbiciu na magazyny.</summary>
        [JsonPropertyName("lines")]
        public List<InterCarsQuoteAvailability> Lines { get; set; } = new();
    }

    public sealed class InterCarsQuoteAvailability
    {
        [JsonPropertyName("location")]
        public string? Location { get; set; }

        [JsonPropertyName("availability")]
        public int Availability { get; set; }
    }

    /// <summary>
    /// Ceny pozycji. <c>listPrice*</c> to cennik katalogowy, <c>customerPrice*</c> - cena po naszych
    /// rabatach, czyli to, co faktycznie płacimy; na niej liczymy marżę oferty.
    /// </summary>
    public sealed class InterCarsPrice
    {
        [JsonPropertyName("currencyCode")]
        public string? CurrencyCode { get; set; }

        [JsonPropertyName("listPriceNet")]
        public decimal ListPriceNet { get; set; }

        [JsonPropertyName("listPriceGross")]
        public decimal ListPriceGross { get; set; }

        [JsonPropertyName("customerPriceNet")]
        public decimal CustomerPriceNet { get; set; }

        [JsonPropertyName("customerPriceGross")]
        public decimal CustomerPriceGross { get; set; }

        [JsonPropertyName("vatPercentage")]
        public decimal VatPercentage { get; set; }
    }
}
