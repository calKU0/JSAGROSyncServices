using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Models
{
    public class OfferDescription
    {
        public int DescriptionId { get; set; }
        public string OfferId { get; set; }
        public string DescType { get; set; }
        public string Content { get; set; }
        public int SectionId { get; set; }
    }
}