using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Models
{
    public class CategoryParameterValue
    {
        public int Id { get; set; }
        public int CategoryParameterId { get; set; }
        public string Value { get; set; }
        public virtual CategoryParameter Parameter { get; set; }
    }
}