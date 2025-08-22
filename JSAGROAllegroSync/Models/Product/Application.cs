using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JSAGROAllegroSync.Models.Product
{
    public class Application
    {
        [Key]
        public int Id { get; set; }

        public int ApplicationId { get; set; }
        public int ParentID { get; set; }
        public string Name { get; set; }

        [ForeignKey("Product")]
        public int ProductId { get; set; }

        public virtual Product Product { get; set; }
    }
}