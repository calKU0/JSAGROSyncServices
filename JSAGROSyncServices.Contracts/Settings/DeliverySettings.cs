namespace JSAGROSyncServices.Contracts.Settings
{
    /// <summary>
    /// Cennik dostawy z Allegro razem z gabarytami paczki, jakie obsługuje.
    /// Wymiary w centymetrach, waga w kilogramach.
    /// </summary>
    public class DeliverySettings
    {
        // Wartości są ułamkowe (np. 31,5 kg) - przy typie int wiązanie konfiguracji
        // po cichu pomijało cały wpis cennika.
        public decimal Width { get; set; }

        public decimal Length { get; set; }

        public decimal Height { get; set; }

        public decimal Weight { get; set; }

        public string DeliveryName { get; set; } = string.Empty;

        /// <summary>
        /// Czy cennik jest objęty Allegro Smart. Tylko w takim razie do ceny oferty doliczamy
        /// koszt wysyłki - przy dostawie poza Smart płaci za nią kupujący, więc wliczenie jej
        /// w cenę podnosiłoby ją dwa razy.
        /// </summary>
        public bool IsSmart { get; set; } = true;
    }
}
