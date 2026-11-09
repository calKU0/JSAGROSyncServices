using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Models;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.Helpers
{
    /// <summary>
    /// Podział patcha oferty na treść (opis, zdjęcia, parametry, dostawa, warunki...) i część
    /// zmienną w każdym cyklu (stan, cena, status publikacji) - jak w serwisie InPost.
    ///
    /// PATCH na Allegro zmienia wyłącznie przesłane pola, więc gdy treść jest taka sama jak przy
    /// ostatnim udanym patchu, wysyłamy samą część zmienną. Pełną strukturę wysyłamy, gdy skrót
    /// treści się zmienił, poprzedni patch się nie udał albo od pełnego minęło <see cref="FullRefreshAfter"/>.
    /// </summary>
    public static class OfferPatchContent
    {
        /// <summary>
        /// Co ile wysyłać pełną strukturę mimo niezmienionej treści. Poprawia ofertę zmienioną ręcznie
        /// na Allegro albo przez Allegro (np. dołożony parametr), czego skrót po naszej stronie nie widzi.
        /// </summary>
        public static readonly TimeSpan FullRefreshAfter = TimeSpan.FromDays(7);

        /// <summary>Pola zmieniane w każdym cyklu - nie wchodzą do skrótu treści.</summary>
        private static readonly string[] VolatileFields = ["stock", "sellingMode", "publication"];

        private static readonly JsonSerializerOptions HashOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>Skrót treści patcha - wszystko poza stanem, ceną i statusem publikacji.</summary>
        public static string ContentHash(ProductOfferRequest request)
        {
            var node = JsonSerializer.SerializeToNode(request, HashOptions)!.AsObject();

            foreach (var field in VolatileFields)
                node.Remove(field);

            var json = node.ToJsonString(HashOptions);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        }

        /// <summary>
        /// Patch z samym stanem, ceną i statusem. Zestaw produktu zostaje: bez productSet Allegro
        /// odrzucało patch (nie widziało podpiętego produktu ani GPSR), a to tylko id i kilka nazw.
        /// </summary>
        public static ProductOfferRequest PriceAndStockOnly(ProductOfferRequest full) => new()
        {
            ProductSet = full.ProductSet,
            Stock = full.Stock,
            SellingMode = full.SellingMode,
            Publication = full.Publication
        };

        /// <summary>Czy wystarczy patch ze stanem i ceną: treść bez zmian od ostatniego udanego pełnego patcha.</summary>
        public static bool IsUnchanged(string contentHash, OfferContentState? lastSent, DateTime utcNow) =>
            lastSent != null
            && string.Equals(lastSent.ContentHash, contentHash, StringComparison.Ordinal)
            && utcNow - lastSent.SentAt < FullRefreshAfter;
    }
}
