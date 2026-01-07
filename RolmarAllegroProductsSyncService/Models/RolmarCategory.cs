using System;
using System.Collections.Generic;
using System.Text;

namespace RolmarAllegroProductsSyncService.Models
{
    public class RolmarCategory
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string Name { get; set; }
    }
}