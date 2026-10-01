using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.DTOs.InterCars
{
    /// <summary>Węzeł katalogu z GET /ic/catalog/category.</summary>
    public sealed class InterCarsCategory
    {
        [JsonPropertyName("categoryId")]
        public string? CategoryId { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }

    /// <summary>Strona wyników GET /ic/catalog/products.</summary>
    public sealed class InterCarsProductsResponse
    {
        [JsonPropertyName("totalResults")]
        public int TotalResults { get; set; }

        [JsonPropertyName("hasNextPage")]
        public bool HasNextPage { get; set; }

        [JsonPropertyName("products")]
        public List<InterCarsProduct> Products { get; set; } = new();
    }

    /// <summary>
    /// Produkt z katalogu. Lista po kategorii zwraca tylko podstawowe pola
    /// (sku, index, brand, opisy, blockedReturn) - wagę, wymiary i EAN-y dokłada
    /// dopiero zapytanie o konkretne SKU.
    /// </summary>
    public sealed class InterCarsProduct
    {
        [JsonPropertyName("sku")]
        public string? Sku { get; set; }

        [JsonPropertyName("index")]
        public string? Index { get; set; }

        [JsonPropertyName("articleNumber")]
        public string? ArticleNumber { get; set; }

        [JsonPropertyName("brand")]
        public string? Brand { get; set; }

        [JsonPropertyName("shortDescription")]
        public string? ShortDescription { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("eans")]
        public List<string>? Eans { get; set; }

        [JsonPropertyName("packageWeight")]
        public string? PackageWeight { get; set; }

        [JsonPropertyName("packageWidth")]
        public string? PackageWidth { get; set; }

        [JsonPropertyName("packageHeight")]
        public string? PackageHeight { get; set; }

        /// <summary>Długość opakowania. API zwraca ją jako packageDepth, kontrakt dopuszcza też packageLength.</summary>
        [JsonPropertyName("packageDepth")]
        public string? PackageDepth { get; set; }

        [JsonPropertyName("packageLength")]
        public string? PackageLength { get; set; }

        [JsonPropertyName("customsCode")]
        public string? CustomsCode { get; set; }

        /// <summary>
        /// Towar bez prawa zwrotu. Uwaga: w liście po kategorii API zwraca tu zawsze <c>false</c> -
        /// wiarygodną wartość podaje zapytanie o pojedyncze SKU oraz cennik (<c>/pricing/quote</c>).
        /// </summary>
        [JsonPropertyName("blockedReturn")]
        public bool BlockedReturn { get; set; }
    }
}
