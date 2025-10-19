using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSyncService.Models.Product
{
    public class ProductFile
    {
        public int Id { get; set; }

        public string Title { get; set; }
        public string Url { get; set; }

        public int ProductId { get; set; }
    }
}