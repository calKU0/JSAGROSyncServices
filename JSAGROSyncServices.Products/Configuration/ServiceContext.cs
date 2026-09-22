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
    }
}
