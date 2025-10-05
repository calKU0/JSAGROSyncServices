using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSync.Models.Product
{
    public class ProductFile
    {
        [Key]
        public int Id { get; set; }

        public string Title { get; set; }
        public string Url { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public virtual Product Product { get; set; }
    }
}