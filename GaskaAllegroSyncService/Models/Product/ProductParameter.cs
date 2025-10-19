using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSyncService.Models.Product
{
    public class ProductParameter
    {
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; }
        public bool IsForProduct { get; set; }

        public virtual Product Product { get; set; }

        public virtual CategoryParameter CategoryParameter { get; set; }
    }
}