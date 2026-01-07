using System;
using System.Collections.Generic;
using System.Text;

namespace JSAGROSyncServices.Shared.Models
{
    public class AllegroImages
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public string Url { get; set; }
        public bool Connected { get; set; } = false;
    }
}