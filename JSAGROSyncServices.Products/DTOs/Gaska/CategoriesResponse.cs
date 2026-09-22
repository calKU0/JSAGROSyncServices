namespace JSAGROSyncServices.Products.DTOs.Gaska
{
    /// <summary>Odpowiedź endpointu /categories - płaska lista kategorii z odwołaniem do rodzica.</summary>
    public class CategoriesResponse
    {
        public List<ApiCategory> Categories { get; set; } = new();

        public int Result { get; set; }

        public string? Message { get; set; }
    }
}
