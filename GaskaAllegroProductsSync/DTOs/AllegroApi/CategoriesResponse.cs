namespace GaskaAllegroProductsSync.DTOs.AllegroApi
{
    public class CategoriesResponse
    {
        public List<CategoryDto> Categories { get; set; }
    }

    public class CategoryDto
    {
        public string Id { get; set; }

        public bool Leaf { get; set; }

        public string Name { get; set; }

        public ParentDto Parent { get; set; }
    }

    public class ParentDto
    {
        public string Id { get; set; }
    }
}