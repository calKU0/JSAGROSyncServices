using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Orders;
using JSAGROSyncServices.Orders.Configuration;

// Konto JSAGRO2 / dostawca Gąska - w dropshippingu jadą tylko oferty z cennika "JAG API",
// reszta trafia na nasz magazyn i stamtąd wysyłamy ją sami.
OrdersServiceHost.Run(
    args,
    new OrderServiceContext
    {
        Account = AllegroAccount.JSAGRO2,
        Company = IntegrationCompany.Gaska,
        WindowsServiceName = "AllegroJSAGRO2GaskaOrdersService",
        MailSender = "Automat JSAGRO2-Gąska"
    },
    new OrderPipeline
    {
        DropshippingDeliveryNames = ["JAG API"],
        WarehouseCourier = "GLS"
    });
