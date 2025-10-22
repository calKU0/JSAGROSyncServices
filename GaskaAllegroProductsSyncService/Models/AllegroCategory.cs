using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AllegroGaskaProductsSyncService.Models
{
    public class AllegroCategory
    {
        public int Id { get; set; }

        public string CategoryId { get; set; }
        public string Name { get; set; }
        public AllegroCategory Parent { get; set; }

        public int? ParentId { get; set; }

        public ICollection<AllegroCategory> Children { get; set; } = new List<AllegroCategory>();
    }
}