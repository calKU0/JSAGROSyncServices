using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products;
using JSAGROSyncServices.Products.Configuration;

// Konto JSAGRO2 / dostawca Gaska - korzysta z danych pobranych przez konto JSAGRO.
ProductsServiceHost.Run(
    args,
    new ServiceContext
    {
        Account = AllegroAccount.JSAGRO2,
        Company = IntegrationCompany.Gaska,
        WindowsServiceName = "AllegroJSAGRO2GaskaProductsService",
        MailSender = "Automat JSAGRO2-Gaska"
    },
    new SyncPipeline
    {
        SyncOfferDetails = true,
        UpdateAllegroCategories = true,
        SyncProductParameters = true
    });
