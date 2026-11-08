namespace JSAGROSyncServices.Contracts.Settings
{
    public class AllegroApiCredentials
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string AuthBaseUrl { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;

        /// <summary>
        /// Czy wolno odświeżać token Allegro. Refresh token jest jednorazowy - odświeżenie na kopii
        /// bazy (np. przy uruchomieniu serwisu lokalnie) unieważnia token w bazie produkcyjnej
        /// i aplikację trzeba sparować od nowa. Przy <c>false</c> serwis używa tylko ważnego tokenu.
        /// </summary>
        public bool AllowTokenRefresh { get; set; } = true;
    }
}