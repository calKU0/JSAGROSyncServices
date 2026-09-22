namespace JSAGROSyncServices.Contracts.DTOs.Allegro
{
    public class CategoriesResponse
    {
        public List<CategoryDto> Categories { get; set; } = new();
    }

    public class ParentDto
    {
        public string? Id { get; set; }
    }
}