using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaskaAllegroSyncService.Models.Product
{
    public class ProductImage
    {
        public int Id { get; set; }

        public string Title { get; set; }
        public string Url { get; set; }
        public string AllegroUrl { get; set; }
        public string AllegroLogoUrl { get; set; }

        public DateTime AllegroExpirationDate { get; set; }
        public int ProductId { get; set; }

        [NotMapped]
        public virtual Product Product { get; set; }
    }
}