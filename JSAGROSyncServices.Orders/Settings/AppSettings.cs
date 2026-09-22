namespace JSAGROSyncServices.Orders.Settings
{
    public class AppSettings
    {
        /// <summary>Godzina, od której serwis realizuje zamówienia.</summary>
        public int StartHour { get; set; }

        /// <summary>Godzina, do której serwis realizuje zamówienia.</summary>
        public int EndHour { get; set; }

        public int LogsExpirationDays { get; set; } = 14;

        public int FetchIntervalMinutes { get; set; } = 10;

        /// <summary>
        /// Cenniki dostawy Allegro obsługiwane przez ten serwis. Zamówienia spoza tych cenników
        /// są pomijane - na koncie są też oferty wystawione ręcznie.
        /// </summary>
        public List<string> AllegroDeliveryNames { get; set; } = new();

        /// <summary>Ile minut odczekać od zakupu przed złożeniem zamówienia u dostawcy.</summary>
        public int OfferProcessingDelayMinutes { get; set; }

        /// <summary>Adresy e-mail powiadomień, rozdzielone średnikiem.</summary>
        public string NotificationsEmail { get; set; } = string.Empty;

        /// <summary>Adres e-mail przekazywany dostawcy przy adresie dostawy.</summary>
        public string DeliveryAddressEmail { get; set; } = string.Empty;
    }
}
