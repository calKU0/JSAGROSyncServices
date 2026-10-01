namespace ServiceManager.Models
{
    /// <summary>Cennik dostawy: gabaryty paczki w centymetrach i waga w kilogramach.</summary>
    public class Delivery
    {
        public decimal Width { get; set; }
        public decimal Length { get; set; }
        public decimal Height { get; set; }
        public decimal Weight { get; set; }
        public string DeliveryName { get; set; } = string.Empty;

        /// <summary>Cennik objęty Allegro Smart - tylko dla takich doliczamy koszt wysyłki do ceny oferty.</summary>
        public bool IsSmart { get; set; } = true;
    }
}
