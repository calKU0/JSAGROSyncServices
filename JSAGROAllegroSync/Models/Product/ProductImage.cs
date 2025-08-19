using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JSAGROAllegroSync.Models
{
    public class ProductImage
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