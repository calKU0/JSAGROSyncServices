using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.InPost.ProductsService.DTOs
{
    // Modele odwzorowują schemat OpenAPI InPost Von Halsky (v1.7.x). Nazwy pól w JSON są
    // camelCase, puste pola nie są wysyłane (patrz InPostClient.JsonOptions).

    public class OfferProposal
    {
        public string? ExternalId { get; set; }
        public ProductInfoProposal Product { get; set; } = new();
        public Stock Stock { get; set; } = new();
        public PriceTag Price { get; set; } = new();
        public GpsrInfo? Gpsr { get; set; }
        public ShippingTime? ShippingTime { get; set; }
        public List<OfferImage>? Images { get; set; }
        public PostSale? PostSale { get; set; }
        public Features? Features { get; set; }
    }

    public class ProductInfoProposal
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public List<AttributeValue>? Attributes { get; set; }
        public string? Model { get; set; }
        public string? Sku { get; set; }
        public string? ManufacturerProductNumber { get; set; }
        public string? Ean { get; set; }
        public Dimension? Dimension { get; set; }
    }

    public class AttributeValue
    {
        public string Id { get; set; } = string.Empty;
        public string? Lang { get; set; }
        public List<string> Values { get; set; } = new();
    }

    /// <summary>Wymiary w milimetrach, waga w gramach.</summary>
    public class Dimension
    {
        public int? Width { get; set; }
        public int? Height { get; set; }
        public int? Length { get; set; }
        public int? Weight { get; set; }

        /// <summary>Skąd wzięto wymiary - tylko do raportu, nie idzie do API.</summary>
        [JsonIgnore]
        public DimensionSource Source { get; set; }
    }

    public enum DimensionSource
    {
        None,
        Product,
        ShippingClass,
        PriceList
    }

    public class Stock
    {
        public int Quantity { get; set; }
        public string Unit { get; set; } = "UNIT";
    }

    public class Money
    {
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "PLN";
    }

    public class PriceTag
    {
        public Money GrossPrice { get; set; } = new();
        public string TaxRateInfo { get; set; } = "23%";
    }

    public class GpsrInfo
    {
        public GpsrManufacturer? Manufacturer { get; set; }
        public bool DoesNotRequireGpsrInfo { get; set; }
        public string? SafetyInformation { get; set; }
    }

    public class GpsrManufacturer
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? CountryCode { get; set; }
        public string? UnstructuredAddress { get; set; }
        public GpsrResponsiblePerson? ResponsiblePersonDetails { get; set; }
    }

    public class GpsrResponsiblePerson
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? CountryCode { get; set; }
        public string? UnstructuredAddress { get; set; }
    }

    public class ShippingTime
    {
        public int DaysToShip { get; set; }
    }

    public class OfferImage
    {
        public string FileName { get; set; } = string.Empty;
        public string FileUrl { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public class PostSale
    {
        public PolicyDescription? ReturnPolicy { get; set; }
        public PolicyDescription? ComplaintPolicy { get; set; }
    }

    public class PolicyDescription
    {
        public string Description { get; set; } = string.Empty;
    }

    public class Features
    {
        public bool Refundable { get; set; } = true;
    }

    // ------------------------------------------------------------------ odpowiedzi

    public class OfferCreated
    {
        public Guid CommandId { get; set; }
        public Guid OfferId { get; set; }
        public string? ExternalId { get; set; }
    }

    public class CommandDetails
    {
        public Guid CommandId { get; set; }
        public Guid? OfferId { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<CommandError>? Errors { get; set; }
    }

    public class CommandError
    {
        public string? FieldName { get; set; }
        public string? ElementId { get; set; }
        public string? Message { get; set; }
    }

    public class OfferDetails
    {
        public OfferMetadata? Metadata { get; set; }
        public OfferSummary? Offer { get; set; }
    }

    public class OfferMetadata
    {
        public List<ValidationError>? ValidationErrors { get; set; }
        public List<object>? RejectionReasons { get; set; }
    }

    public class ValidationError
    {
        public string ValidationCode { get; set; } = string.Empty;
        public string? ValidationMessage { get; set; }
    }

    public class OfferSummary
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ExternalId { get; set; }
    }

    public class OfferPricePatchItem
    {
        public Guid OfferId { get; set; }
        public Money Price { get; set; } = new();
    }

    public class OfferStockPatchItem
    {
        public Guid OfferId { get; set; }
        public Stock Stock { get; set; } = new();
    }

    // ------------------------------------------------------------------ kategorie

    public class InPostCategory
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Leaf { get; set; }
        public bool DoesNotRequireGpsrInfo { get; set; }
        public List<InPostCategory>? Children { get; set; }
    }

    public class AttributeDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "TEXT_VALUE";
        public string? Lang { get; set; }
        public string ExpectedValue { get; set; } = "ANY";
        public string Name { get; set; } = string.Empty;
        public AttributeDictionary? Dictionary { get; set; }
    }

    public class AttributeDictionary
    {
        public List<AttributeDictionaryOption> Options { get; set; } = new();
    }

    public class AttributeDictionaryOption
    {
        public string Id { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool Active { get; set; }
    }

    public class InPostErrorResponse
    {
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public List<InPostErrorDetail>? Details { get; set; }
    }

    public class InPostErrorDetail
    {
        public string? Field { get; set; }
        public string? Detail { get; set; }
    }

    public class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
