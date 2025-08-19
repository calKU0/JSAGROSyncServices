using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JSAGROAllegroSync.Models
{
    public class ProductCategory
    {
        [Key]
        public int Id { get; set; }

        public int CategoryId { get; set; }
        public int ParentID { get; set; }
        public string Name { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public virtual Product Product { get; set; }
    }
}