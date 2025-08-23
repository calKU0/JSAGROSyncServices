using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs.AllegroApiResponses
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