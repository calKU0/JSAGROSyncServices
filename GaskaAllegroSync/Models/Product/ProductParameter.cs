using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSync.Models.Product
{
    public class ProductParameter
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; }
        public bool IsForProduct { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product Product { get; set; }

        [ForeignKey(nameof(CategoryParameterId))]
        public virtual CategoryParameter CategoryParameter { get; set; }
    }
}