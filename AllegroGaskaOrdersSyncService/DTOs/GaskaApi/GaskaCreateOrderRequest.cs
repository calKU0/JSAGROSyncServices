﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AllegroGaskaOrdersSyncService.DTOs.GaskaApi
{
    public class GaskaCreateOrderRequest
    {
        [JsonPropertyName("deliveryAddressId")]
        public int DeliveryAddressId { get; set; }

        [JsonPropertyName("deliveryMethod")]
        public string DeliveryMethod { get; set; } = string.Empty;

        [JsonPropertyName("customerNumber")]
        public string? CustomerNumber { get; set; }

        [JsonPropertyName("notice")]
        public string? Notice { get; set; }

        [JsonPropertyName("dropshippingAmount")]
        public decimal? DropshippingAmount { get; set; }

        [JsonPropertyName("items")]
        public List<GaskaCreateOrderItemRequest> Items { get; set; } = new();
    }
}