using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products;
using JSAGROSyncServices.Products.Configuration;

// Konto JSAGRO2 / dostawca Rolmar - korzysta z danych pobranych przez konto JSAGRO.
ProductsServiceHost.Run(
    args,
    new ServiceContext
    {
        Account = AllegroAccount.JSAGRO2,
        Company = IntegrationCompany.Rolmar,
        WindowsServiceName = "AllegroJSAGRO2RolmarProductsService",
        MailSender = "Automat JSAGRO2-Rolmar"
    },
    new SyncPipeline());
