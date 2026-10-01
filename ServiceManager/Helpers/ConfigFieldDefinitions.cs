using ServiceManager.Enums;
using ServiceManager.Models;

namespace ServiceManager.Helpers
{
    public static class ConfigFieldDefinitions
    {
        public static readonly List<ConfigField> AllFields = new()
        {
            // Dane dostępowe do API są ustawiane przy wdrożeniu i nie pokazujemy ich w konfiguratorze.
            // Gąska API
            new ConfigField { Key = "GaskaApiCredentials:BaseUrl", Label = "Adres API Gąska", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Acronym", Label = "Akronim", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Person", Label = "Osoba", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Password", Label = "Hasło", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:Key", Label = "Klucz API", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsPerPage", Label = "Produkty na stronę", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsInterval", Label = "Interwał pobierania produktów", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductPerDay", Label = "Produkty dziennie", Group = "Gąska API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductInterval", Label = "Interwał pobierania szczegółów", Group = "Gąska API", IsEnabled = false, IsVisable = false },

            // Inter Cars API
            new ConfigField { Key = "InterCarsApiCredentials:BaseUrl", Label = "Adres API Inter Cars", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:AuthUrl", Label = "Adres autoryzacji", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:ClientId", Label = "Client ID", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:ClientSecret", Label = "Client Secret", Group = "Inter Cars API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:Warehouses", Label = "Magazyny do sumowania stanów", Group = "Inter Cars API", FieldType = ConfigFieldType.StringList, Description = "Stan produktu to suma dostępności z tych magazynów Inter Cars." },
            new ConfigField { Key = "InterCarsApiCredentials:ProductDetailsPerRun", Label = "Limit szczegółów na przebieg", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, IsVisable = true, Description = "Ile szczegółów produktów pobrać w jednym cyklu. Najpierw produkty bez szczegółów, potem najdawniej odświeżane. 0 = bez limitu, czyli cały katalog w jednym cyklu." },
            new ConfigField { Key = "InterCarsApiCredentials:ProductDetailsRefreshDays", Label = "Co ile dni odświeżać szczegóły produktu", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, Description = "Waga, wymiary i EAN przychodzą po jednym produkcie na zapytanie. Najpierw pobierane są produkty bez szczegółów, potem te odświeżane dawniej niż podana liczba dni temu." },
            new ConfigField { Key = "InterCarsApiCredentials:DataBaseUrl", Label = "Adres wymiany plików CSV", Group = "Inter Cars pliki CSV", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:DataCustomerNumber", Label = "Numer klienta (katalog z plikami)", Group = "Inter Cars pliki CSV", Description = "Nazwa katalogu z plikami CSV na serwerze wymiany Inter Cars." },
            new ConfigField { Key = "InterCarsApiCredentials:DataUser", Label = "Użytkownik wymiany plików", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:DataPassword", Label = "Hasło wymiany plików", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:PicturesCustomerNumber", Label = "Numer klienta dla zdjęć", Group = "Inter Cars pliki CSV", Description = "Katalog z plikiem zdjęć. Konto rolnicze go nie ma - zdjęcia idą z konta z pełnym katalogiem. Puste = to samo konto co wyżej." },
            new ConfigField { Key = "InterCarsApiCredentials:PicturesUser", Label = "Użytkownik dla zdjęć", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:PicturesPassword", Label = "Hasło dla zdjęć", Group = "Inter Cars pliki CSV", IsVisable = false },
            new ConfigField { Key = "InterCarsApiCredentials:Parallelism", Label = "Równoległych zapytań", Group = "Inter Cars API", FieldType = ConfigFieldType.Int, IsVisable = false },

            // Allegro API
            new ConfigField { Key = "AllegroApiCredentials:BaseUrl", Label = "Adres API Allegro", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:AuthBaseUrl", Label = "Adres Autoryzacji Allegro", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientName", Label = "Nazwa klienta", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientId", Label = "Client ID", Group = "Allegro API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientSecret", Label = "Client Secret", Group = "Allegro API", IsEnabled = false, IsVisable = false },

            // ERLI API
            new ConfigField { Key = "ErliApiCredentials:BaseUrl", Label = "Adres API Erli", Group = "Erli API", IsEnabled = false, IsVisable = false },
            new ConfigField { Key = "ErliApiCredentials:ApiKey", Label = "Klucz API", Group = "Erli API", IsEnabled = false, IsVisable = false },

            // Courier Settings
            new ConfigField { Key = "CourierSettings:DpdFinalOrderHour", Label = "Ostateczna godzina DPD", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "CourierSettings:FedexFinalOrderHour", Label = "Ostateczna godzina Fedex", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "CourierSettings:GlsFinalOrderHour", Label = "Ostateczna godzina GLS", Group = "Ustawienia Kurierów", IsEnabled = true, FieldType = ConfigFieldType.Int },

            // Price Settings
            new ConfigField { Key = "PriceSettings:AllegroMarginUnder5PLN", Label = "Prowizja allegro poniżej 5 PLN", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:AllegroMarginBetween5and1000PLNPercent", Label = "Prowizja allegro 5-1000 PLN (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:AllegroMarginMoreThan1000PLN", Label = "Prowizja allegro powyżej 1000 PLN", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:BulkyDeliveryPriceNet", Label = "Cena netto wysyłki gabarytowej", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:CustomDeliveryPriceNet", Label = "Cena netto wysyłki niestandardowej", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:DropshippingPriceNet", Label = "Cena netto dropshippingu", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:OversizeSurcharge", Label = "Dopłata za produkt ponadgabarytowy (PLN)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Doliczana do ceny oferty, gdy którykolwiek wymiar produktu przekracza próg. 0 = bez dopłaty." },
            new ConfigField { Key = "PriceSettings:OversizeThresholdCm", Label = "Próg ponadgabarytu (cm)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "PriceSettings:MaxPriceDropPercent", Label = "Maksymalny spadek ceny (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Dotyczy ceny zakupu u dostawcy. Po skokowym spadku powyżej tej wartości cena oferty nie jest aktualizowana, a produkt nie jest wystawiany - zgłaszamy to w logu i mailem. Blokada znika, gdy cena zakupu wróci do poprzedniego poziomu. 0 = bez ograniczenia." },

            // AppSettings
            new ConfigField { Key = "AppSettings:StartHour", Label = "Godzina rozpoczęcia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:EndHour", Label = "Godzina zakończenia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:CategoriesId", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.IntList, CategoryCompany = 2, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:CategoriesName", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, CategoryCompany = 1, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:CategoriesKey", Label = "Synchronizowane kategorie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, CategoryCompany = 3, Description = "Pobierane są i trafiają na Allegro tylko produkty z tych kategorii (razem z podkategoriami), i tylko te, które są w plikach CSV Inter Cars - a te zawierają wyłącznie asortyment rolniczy (stąd AGRO na początku ścieżki). Usunięcie kategorii kończy (ENDED) oferty jej produktów." },
            new ConfigField { Key = "AppSettings:DeliveriesWithoutPriceUpdate", Label = "Cenniki dostaw zarządzane ręcznie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Oferty z tych cenników mają aktualizowany tylko stan i opis. Serwis nie zmienia im ceny, cennika dostawy ani czasu realizacji." },
            new ConfigField { Key = "AppSettings:PriceDropAlertEmail", Label = "Email do powiadomień o spadku ceny", IsVisable = false, Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:UploadParallelism", Label = "Ile produktów wysyłać równolegle", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:CourierPriceSurcharge", Label = "Dopłata do ceny przy wysyłce kurierem (PLN)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:ArchiveAfterDaysMissing", Label = "Po ilu dniach braku u dostawcy zakończyć ofertę", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int, Description = "Produkt, którego dostawca nie zwraca od tylu dni, jest oznaczany jako archiwalny: nie trafia na Allegro, a jego oferta jest kończona (ENDED). Karencja chroni przed zakończeniem ofert całego asortymentu przez jedno nieudane pobranie od dostawcy." },
            new ConfigField { Key = "AppSettings:AllegroParallelism", Label = "Równoległe zapytania do Allegro", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int, Description = "Ile zapytań do Allegro naraz. Za dużo kończy się błędami 429 i ponowieniami, które i tak spowalniają cykl. Zalecane 4-10." },
            new ConfigField { Key = "AppSettings:MinProductStock", Label = "Minimalny stan produktu", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:MinProductPriceNet", Label = "Minimalna cena netto produktu (u dostawcy)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:BundleProductsUnderPriceNet", Label = "Łącz w zestawy poniżej X PLN", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:LogsExpirationDays", Label = "Ilość dni zachowania logów", IsVisable = false, Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:FetchIntervalMinutes", Label = "Co ile wywoływać synchronizację (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:OfferProcessingDelayMinutes", Label = "Opóźnienie złożenia zamówienia (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:NotificationsEmail", Label = "Adresy email do powiadomień (rodzielone średnikiem)", IsVisable = false, Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:AllegroDeliveryNames", Label = "Nazwy cenników dostawy Allegro", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Serwis obsługuje tylko zamówienia z tych cenników. Oferty wystawione ręcznie, spoza listy, są pomijane." },
            new ConfigField { Key = "AppSettings:DeliveryAddressEmail", Label = "Email podawany w adresie dostawy", IsVisable = false, Group = "Ustawienia serwisu" },

            // Allegro Settings
            new ConfigField { Key = "AllegroSettings:AllegroHandlingTime", Label = "Czas realizacji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroHandlingTimeCustomProducts", Label = "Czas realizacji produktów niestandardowych", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroSafetyMeasures", Label = "Tekst bezpieczeństwa", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroWarranty", Label = "Nazwa polityki gwarancji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroReturnPolicy", Label = "Nazwa polityki zwrotów", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroImpliedWarranty", Label = "Nazwa polityki reklamacji", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroResponsiblePerson", Label = "Odpowiedzialna osoba", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:AllegroResponsibleProducer", Label = "Odpowiedzialny producent", Group = "Ustawienia Allegro" },
            new ConfigField { Key = "AllegroSettings:DefaultPartsManufacturer", Label = "Domyślny producent części", Group = "Ustawienia Allegro", Description = "Wstawiany w parametr „Producent części”, gdy dostawca go nie podaje." },
        };
    }
}