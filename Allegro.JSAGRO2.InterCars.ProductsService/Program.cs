using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Products;
using JSAGROSyncServices.Products.Configuration;

// Konto JSAGRO2 / dostawca Inter Cars - jedyne konto tego dostawcy, więc pobiera wszystkie dane samo.
ProductsServiceHost.Run(
    args,
    new ServiceContext
    {
        Account = AllegroAccount.JSAGRO2,
        Company = IntegrationCompany.InterCars,
        WindowsServiceName = "AllegroJSAGRO2InterCarsProductsService",
        MailSender = "Automat JSAGRO2-InterCars"
    },
    new SyncPipeline
    {
        SyncAllegroDictionaries = true,
        FetchSupplierProducts = true,
        FetchSupplierStock = true,
        // Adresy zdjęć są wyłącznie w plikach wymiany CSV - API katalogu ich nie zwraca.
        FetchSupplierImages = true,
        FetchSupplierProductDetailsDaily = true,
        // Katalog ma kilka tysięcy kategorii - pełne drzewo do wyboru w konfiguratorze budujemy raz na dobę.
        FetchSupplierCategoryTreeDaily = true,
        // Waga i wymiary przychodzą dopiero ze szczegółami, a bez nich nie da się wybrać cennika dostawy.
        RequireProductDetails = true,
        UpdateAllegroCategoriesDaily = true,
        SyncOfferDetails = true,
        SearchAllegroProducts = true,
        SyncProductParameters = true
    });
