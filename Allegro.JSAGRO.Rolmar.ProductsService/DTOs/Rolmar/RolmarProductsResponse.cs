using System.Text.Json.Serialization;
using static JSAGROSyncServices.Shared.Helpers.Converters;

namespace Allegro.JSAGRO.Rolmar.ProductsService.DTOs
{
    public class RolmarProductReponse
    {
        [JsonPropertyName("result")]
        public List<ProductResult> Products { get; set; }
    }

    public class ProductResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("ean")]
        public string Ean { get; set; }

        [JsonPropertyName("productIndex")]
        public string ProductIndex { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("substitutes")]
        public string Substitutes { get; set; }

        [JsonPropertyName("fits")]
        public string Fits { get; set; }

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("mainPhoto")]
        public string MainPhoto { get; set; }

        [JsonPropertyName("weight")]
        public string Weight { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("erp_package")]
        public string ErpPackage { get; set; }

        [JsonPropertyName("brand")]
        public string Brand { get; set; }

        [JsonPropertyName("cn")]
        public string Cn { get; set; }

        [JsonPropertyName("specifications")]
        [JsonConverter(typeof(EmptyStringToListConverter<ProductSpecification>))]
        public List<ProductSpecification>? Specifications { get; set; }

        [JsonPropertyName("retailPrice")]
        public string RetailPrice { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; }

        [JsonPropertyName("categories")]
        [JsonConverter(typeof(EmptyStringToListConverter<string>))]
        public List<string> Categories { get; set; }

        [JsonPropertyName("cubature")]
        public string Cubature { get; set; }

        [JsonPropertyName("currency")]
        public string Currency { get; set; }
    }

    public class ProductSpecification
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("value")]
        public string Value { get; set; }

        [JsonPropertyName("unit_name")]
        public string UnitName { get; set; }
    }
}