namespace JSAGROSyncServices.Infrastructure.Services
{
    /// <summary>
    /// Brak ważnego tokenu Allegro i nie da się go odświeżyć - konto musi zostać ponownie
    /// autoryzowane przez użytkownika pod adresem <see cref="AuthorizationUrl"/>.
    /// </summary>
    public class AllegroAuthorizationRequiredException : Exception
    {
        public AllegroAuthorizationRequiredException(string authorizationUrl)
            : base($"Allegro authorization required. Open {authorizationUrl} to authorize this account.")
        {
            AuthorizationUrl = authorizationUrl;
        }

        public string AuthorizationUrl { get; }
    }
}
