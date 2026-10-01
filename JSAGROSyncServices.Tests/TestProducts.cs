using JSAGROSyncServices.Contracts.Models;
using JSAGROSyncServices.Contracts.Settings;

namespace JSAGROSyncServices.Tests
{
    /// <summary>Budowanie produktów i cenników na potrzeby testów - skraca same przypadki do treści.</summary>
    internal static class TestProducts
    {
        /// <summary>Cenniki w układzie, jakiego serwis używa na produkcji: mały, średni, duży.</summary>
        public static List<DeliverySettings> Deliveries() =>
        [
            new() { Width = 41, Length = 64, Height = 38, Weight = 20m, DeliveryName = "Mały" },
            new() { Width = 60, Length = 100, Height = 60, Weight = 30m, DeliveryName = "Średni" },
            new() { Width = 60, Length = 150, Height = 60, Weight = 31.5m, DeliveryName = "Duży" }
        ];

        /// <param name="weight">Waga produktu w kilogramach; 0 oznacza, że dostawca jej nie podał.</param>
        /// <param name="deliveryType">0 = zwykła paczka, pozostałe wartości = gabaryt / towar niestandardowy.</param>
        /// <param name="specifications">Krotki (nazwa, wartość, jednostka) - tak jak przychodzą od dostawcy.</param>
        public static RolmarProduct Product(
            float weight = 0,
            int deliveryType = 0,
            params (string Name, string Value, string? Unit)[] specifications) => new()
            {
                Id = 1,
                Code = "TEST",
                Weight = weight,
                DeliveryType = deliveryType,
                Specifications = specifications
                    .Select(s => new ProductSpecification { Name = s.Name, Value = s.Value, UnitName = s.Unit! })
                    .ToList()
            };
    }
}
