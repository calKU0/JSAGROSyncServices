using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Models
{
    public class AllegroCategory
    {
        [Key]
        public int Id { get; set; }

        public string CategoryId { get; set; }
        public string Name { get; set; }

        [ForeignKey("ParentId")]
        public AllegroCategory Parent { get; set; }

        public int? ParentId { get; set; }

        public ICollection<AllegroCategory> Children { get; set; } = new List<AllegroCategory>();
    }
}