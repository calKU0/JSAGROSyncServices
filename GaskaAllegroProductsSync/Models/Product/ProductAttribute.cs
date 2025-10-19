using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroProductsSync.Models.Product
{
    public class ProductAttribute
    {
        public int Id { get; set; }

        public int AttributeId { get; set; }
        public string AttributeName { get; set; }
        public string AttributeValue { get; set; }

        public int ProductId { get; set; }
    }
}