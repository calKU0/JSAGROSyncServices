using System;
using System.ComponentModel.DataAnnotations;

namespace GaskaAllegroSync.Models
{
    public class AllegroTokenEntity
    {
        [Key]
        public int Id { get; set; }

        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiryDateUtc { get; set; }
    }
}