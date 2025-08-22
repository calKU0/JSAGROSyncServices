using JSAGROAllegroSync.Models.Product;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Models
{
    public class CategoryParameter
    {
        public int ParameterId { get; set; }
        public int CategoryId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Required { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
    }
}