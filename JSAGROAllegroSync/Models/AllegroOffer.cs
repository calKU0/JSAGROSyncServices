using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Models
{
    public class AllegroOffer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string Id { get; set; }

        public int? ProductId { get; set; }
        public string ExternalId { get; set; }

        public string Name { get; set; }
        public int CategoryId { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public int WatchersCount { get; set; }
        public int VisitsCount { get; set; }
        public string Status { get; set; }
        public string DeliveryName { get; set; }
        public DateTime StartingAt { get; set; }

        [ForeignKey(nameof(ProductId))]
        public virtual Product.Product Product { get; set; }
    }
}