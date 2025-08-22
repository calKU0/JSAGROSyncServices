using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JSAGROAllegroSync.Models.Product
{
    public class CrossNumber
    {
        [Key]
        public int Id { get; set; }

        public string CrossNumberValue { get; set; }
        public string CrossManufacturer { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public virtual Product Product { get; set; }
    }
}