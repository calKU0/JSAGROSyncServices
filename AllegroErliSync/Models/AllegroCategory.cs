using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Models
{
    public class AllegroCategory
    {
        public int Id { get; set; }
        public string CategoryId { get; set; }
        public string Name { get; set; }
        public int? ParentId { get; set; }
    }
}