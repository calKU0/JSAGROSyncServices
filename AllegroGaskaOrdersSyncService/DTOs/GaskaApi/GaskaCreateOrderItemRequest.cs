﻿using System.Text.Json.Serialization;

namespace AllegroGaskaOrdersSyncService.DTOs.GaskaApi
{
    public class GaskaCreateOrderItemRequest
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("qty")]
        public string Qty { get; set; } = string.Empty;

        [JsonPropertyName("Notice")]
        public string? Notice { get; set; }
    }
}