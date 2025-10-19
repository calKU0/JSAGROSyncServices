namespace GaskaAllegroProductsSync.DTOs.AllegroApiResponses
{
    public class MatchingCategoriesResponse
    {
        public List<CategoryDto> MatchingCategories { get; set; }
    }

    public class CategoryDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public CategoryDto Parent { get; set; }
    }
}