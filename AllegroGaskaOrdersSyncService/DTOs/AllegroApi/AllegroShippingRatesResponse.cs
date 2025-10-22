﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.AllegroApi
{
    public class ShippingRatesReponse
    {
        [JsonPropertyName("shippingRates")]
        public List<ShippingRate> ShippingRates { get; set; }

        public class ShippingRate
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("features")]
            public Features Features { get; set; }

            [JsonPropertyName("marketplaces")]
            public List<Marketplace> Marketplaces { get; set; }
        }

        public class Features
        {
            [JsonPropertyName("managedByAllegro")]
            public bool ManagedByAllegro { get; set; }
        }

        public class Marketplace
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
        }
    }
}