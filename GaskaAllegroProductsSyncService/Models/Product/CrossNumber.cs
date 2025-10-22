using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AllegroGaskaProductsSyncService.Models.Product
{
    public class CrossNumber
    {
        public int Id { get; set; }

        public string CrossNumberValue { get; set; }
        public string CrossManufacturer { get; set; }

        public int ProductId { get; set; }
    }
}