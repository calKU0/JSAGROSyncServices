using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Orders;
using JSAGROSyncServices.Orders.Configuration;

// Konto JSAGRO / dostawca Gąska - całość sprzedaży jedzie w dropshippingu prosto do klienta.
OrdersServiceHost.Run(
    args,
    new OrderServiceContext
    {
        Account = AllegroAccount.JSAGRO,
        Company = IntegrationCompany.Gaska,
        WindowsServiceName = "AllegroJSAGROGaskaOrdersService",
        MailSender = "Automat JSAGRO-Gąska"
    },
    new OrderPipeline());
