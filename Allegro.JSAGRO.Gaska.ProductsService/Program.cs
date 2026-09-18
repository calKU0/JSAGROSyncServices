using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products;
using JSAGROSyncServices.Products.Configuration;

// Konto JSAGRO / dostawca Gaska - to konto pobiera dane od dostawcy dla obu kont.
ProductsServiceHost.Run(
    args,
    new ServiceContext
    {
        Account = AllegroAccount.JSAGRO,
        Company = IntegrationCompany.Gaska,
        WindowsServiceName = "AllegroJSAGROGaskaProductsService",
        MailSender = "Automat JSAGRO-Gaska"
    },
    new SyncPipeline
    {
        SyncAllegroDictionaries = true,
        FetchSupplierProducts = true,
        FetchSupplierProductDetailsDaily = true,
        UpdateAllegroCategoriesDaily = true,
        SyncOfferDetails = true,
        SyncProductParameters = true
    });
