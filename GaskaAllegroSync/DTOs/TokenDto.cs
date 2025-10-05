using System;

namespace GaskaAllegroSync.DTOs
{
    public class TokenDto
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime ExpiryDateUtc { get; set; }

        public bool IsExpired() => DateTime.UtcNow >= ExpiryDateUtc.AddHours(-2);
    }
}