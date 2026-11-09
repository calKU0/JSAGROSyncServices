using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Products.Configuration
{
    /// <summary>
    /// Tożsamość serwisu: konto Allegro i dostawca, dla którego pracuje.
    /// Zastępuje dotychczasowe statyczne ServiceConstants powielane w każdym projekcie.
    /// </summary>
    public sealed class ServiceContext
    {
        public required AllegroAccount Account { get; init; }

        public required IntegrationCompany Company { get; init; }

        /// <summary>Nazwa usługi Windows.</summary>
        public required string WindowsServiceName { get; init; }

        /// <summary>Nadawca maili wysyłanych przez serwis.</summary>
        public required string MailSender { get; init; }

        public string ImagesFolder { get; init; } = @"C:\Program Files (x86)\Api Sync Services\Product_Images";

        /// <summary>
        /// Parametry produktu bierzemy wyłącznie z produktu z katalogu Allegro (Rolmar, oba konta).
        /// Nie wyliczamy ich, nie zapisujemy w bazie i nie wysyłamy - sami uzupełniamy tylko
        /// obowiązkowe parametry oferty. Produkt z katalogu bez kompletu obowiązkowych parametrów
        /// zastępujemy innym, a gdy takiego nie ma, kończymy ofertę i odpinamy produkt.
        /// </summary>
        public bool ProductParametersFromCatalog => Company == IntegrationCompany.Rolmar;
    }
}
