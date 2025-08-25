using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.DTOs.AllegroApi
{
    public class ShippingRatesReponse
    {
        public List<ShippingRate> ShippingRates { get; set; }
    }

    public class ShippingRate
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public Features Features { get; set; }
        public List<Marketplace> Marketplaces { get; set; }
    }

    public class Features
    {
        public bool ManagedByAllegro { get; set; }
    }

    public class Marketplace
    {
        public string Id { get; set; }
    }
}