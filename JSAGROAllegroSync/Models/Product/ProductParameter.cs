using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Models.Product
{
    public class ProductParameter
    {
        [Key]
        public int Id { get; set; }

        public int ProductId { get; set; }
        public int ParameterId { get; set; }
        public string Value { get; set; }

        public virtual Product Product { get; set; }
    }
}