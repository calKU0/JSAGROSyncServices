using System;
using System.ComponentModel.DataAnnotations;

namespace GaskaAllegroSyncService.Models
{
    public class AllegroTokenEntity
    {
        public int Id { get; set; }

        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiryDateUtc { get; set; }
    }
}