using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroProductsSync.Models.Product
{
    public class Application
    {
        public int Id { get; set; }

        public int ApplicationId { get; set; }
        public int ParentID { get; set; }
        public string Name { get; set; }

        public int ProductId { get; set; }
    }
}