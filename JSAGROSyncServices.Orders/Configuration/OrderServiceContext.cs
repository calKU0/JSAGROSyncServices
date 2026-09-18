using JSAGROSyncServices.Contracts.Data.Enums;

namespace JSAGROSyncServices.Orders.Configuration
{
    /// <summary>
    /// Tożsamość serwisu zamówień: konto Allegro i dostawca, dla którego pracuje.
    /// Zastępuje statyczne ServiceConstants powielane w każdym projekcie.
    /// </summary>
    public sealed class OrderServiceContext
    {
        public required AllegroAccount Account { get; init; }

        public required IntegrationCompany Company { get; init; }

        /// <summary>Nazwa usługi Windows.</summary>
        public required string WindowsServiceName { get; init; }

        /// <summary>Nadawca maili wysyłanych przez serwis.</summary>
        public required string MailSender { get; init; }
    }
}
