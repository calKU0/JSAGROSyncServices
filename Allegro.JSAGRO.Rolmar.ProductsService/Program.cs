using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products;
using JSAGROSyncServices.Products.Configuration;

// Konto JSAGRO / dostawca Rolmar - to konto pobiera dane od dostawcy dla obu kont.
ProductsServiceHost.Run(
    args,
    new ServiceContext
    {
        Account = AllegroAccount.JSAGRO,
        Company = IntegrationCompany.Rolmar,
        WindowsServiceName = "AllegroJSAGRORolmarProductsService",
        MailSender = "Automat JSAGRO-Rolmar"
    },
    new SyncPipeline
    {
        FetchSupplierProducts = true,
        FetchSupplierStock = true,
        FetchSupplierImages = true,
        SearchAllegroProducts = true,
        SyncProductParameters = true
    });
