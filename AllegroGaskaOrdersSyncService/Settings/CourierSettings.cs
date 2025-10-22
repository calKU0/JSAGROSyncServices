using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.Settings
{
    public class CourierSettings
    {
        public int DpdFinalOrderHour { get; set; }
        public int FedexFinalOrderHour { get; set; }
        public int GlsFinalOrderHour { get; set; }
    }
}