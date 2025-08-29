using System.ComponentModel.DataAnnotations;

namespace JSAGROAllegroSync.Models.Product
{
    public class ProductParameter
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; }
        public bool IsForProduct { get; set; }
        public virtual Product Product { get; set; }
        public virtual CategoryParameter CategoryParameter { get; set; }
    }
}