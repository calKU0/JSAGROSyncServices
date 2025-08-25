using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs.Settings
{
    public class AppSettings
    {
        public List<int> CategoriesId { get; set; }
        public int LogsExpirationDays { get; set; }
        public int FetchIntervalMinutes { get; set; }
        public decimal OwnMarginPercent { get; set; }
        public decimal AllegroMarginUnder5PLN { get; set; }
        public decimal AllegroMarginBetween5and1000PLNPercent { get; set; }
        public decimal AllegroMarginMoreThan1000PLN { get; set; }
        public decimal AddPLNToBulkyProducts { get; set; }
        public decimal AddPLNToCustomProducts { get; set; }
        public string AllegroDeliveryName { get; set; }
        public string AllegroHandlingTime { get; set; }
        public string AllegroHandlingTimeCustomProducts { get; set; }
        public string AllegroSafetyMeasures { get; set; }
        public string AllegroWarranty { get; set; }
        public string AllegroReturnPolicy { get; set; }
        public string AllegroImpliedWarranty { get; set; }
        public string AllegroResponsiblePerson { get; set; }
        public string AllegroResponsibleProducer { get; set; }
    }
}