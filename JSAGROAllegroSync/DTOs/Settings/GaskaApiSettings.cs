using System.Collections.Generic;

namespace JSAGROAllegroSync.DTOs
{
    public class GaskaApiSettings
    {
        public string BaseUrl { get; set; }
        public string Acronym { get; set; }
        public string Person { get; set; }
        public string Password { get; set; }
        public string ApiKey { get; set; }
        public List<int> CategoriesId { get; set; }
        public int ProductsPerPage { get; set; }
        public int ProductsInterval { get; set; }
        public int ProductPerDay { get; set; }
        public int ProductInterval { get; set; }
    }
}