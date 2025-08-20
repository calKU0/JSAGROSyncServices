using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs
{
    public class ProductDto
    {
        public int Id { get; set; }
        public string CodeGaska { get; set; }
        public string CodeCustomer { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; }
        public string Ean { get; set; }
        public string SupplierName { get; set; }
        public string SupplierLogo { get; set; }
        public string Description { get; set; }
        public string TechnicalDetails { get; set; }
        public decimal NetPrice { get; set; }
        public decimal GrossPrice { get; set; }
        public string CurrencyPrice { get; set; }
        public float NetWeight { get; set; }
        public float InStock { get; set; }
        public string CrossNumbers { get; set; }
        public int SuggestedCategoryId { get; set; }
        public List<ApplicationDto> Applications { get; set; }
        public List<ParameterDto> Parameters { get; set; }
        public List<ImageDto> Images { get; set; }
    }
}