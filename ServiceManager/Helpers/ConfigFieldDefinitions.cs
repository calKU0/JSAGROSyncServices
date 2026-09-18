using ServiceManager.Enums;
using ServiceManager.Models;

namespace ServiceManager.Helpers
{
    public static class ConfigFieldDefinitions
    {
        public static readonly List<ConfigField> AllFields = new()
        {
            // Gąska API
            new ConfigField { Key = "GaskaApiCredentials:BaseUrl", Label = "Adres API Gąska", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:Acronym", Label = "Akronim", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:Person", Label = "Osoba", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:Password", Label = "Hasło", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:Key", Label = "Klucz API", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsPerPage", Label = "Produkty na stronę", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductsInterval", Label = "Interwał pobierania produktów", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductPerDay", Label = "Produkty dziennie", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApiCredentials:ProductInterval", Label = "Interwał pobierania szczegółów", Group = "Gąska API", IsEnabled = false },

            // Allegro API
            new ConfigField { Key = "AllegroApiCredentials:BaseUrl", Label = "Adres API Allegro", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApiCredentials:AuthBaseUrl", Label = "Adres Autoryzacji Allegro", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientName", Label = "Nazwa klienta", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientId", Label = "Client ID", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApiCredentials:ClientSecret", Label = "Client Secret", Group = "Allegro API", IsEnabled = false },

            // ERLI API
            new ConfigField { Key = "ErliApiCredentials:BaseUrl", Label = "Adres API Erli", Group = "Erli API", IsEnabled = false },
            new ConfigField { Key = "ErliApiCredentials:ApiKey", Label = "Klucz API", Group = "Erli API", IsEnabled = false },

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
            new ConfigField { Key = "PriceSettings:MaxPriceDropPercent", Label = "Maksymalny spadek ceny (%)", Group = "Narzuty", FieldType = ConfigFieldType.Decimal, Description = "Powyżej tego spadku cena nie jest aktualizowana, tylko zgłaszana w logu i mailem. 0 = bez ograniczenia." },

            // AppSettings
            new ConfigField { Key = "AppSettings:StartHour", Label = "Godzina rozpoczęcia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:EndHour", Label = "Godzina zakończenia synchronizacji", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int},
            new ConfigField { Key = "AppSettings:CategoriesId", Label = "ID synchronizowanych kategorii", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.IntList, Description = "Oferty zakładane i aktualizowane są tylko dla produktów z tych kategorii. Usunięcie kategorii kończy (ENDED) oferty produktów, które do niej należały." },
            new ConfigField { Key = "AppSettings:CategoriesName", Label = "Nazwy synchronizowanych kategorii", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Oferty zakładane i aktualizowane są tylko dla produktów z tych kategorii. Usunięcie kategorii kończy (ENDED) oferty produktów, które do niej należały." },
            new ConfigField { Key = "AppSettings:DeliveriesWithoutPriceUpdate", Label = "Cenniki dostaw zarządzane ręcznie", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Oferty z tych cenników mają aktualizowany tylko stan i opis. Serwis nie zmienia im ceny, cennika dostawy ani czasu realizacji." },
            new ConfigField { Key = "AppSettings:PriceDropAlertEmail", Label = "Email do powiadomień o spadku ceny", Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:UploadParallelism", Label = "Ile produktów wysyłać równolegle", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:CourierPriceSurcharge", Label = "Dopłata do ceny przy wysyłce kurierem (PLN)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:MinProductStock", Label = "Minimalny stan produktu", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:MinProductPriceNet", Label = "Minimalna cena netto produktu (w Gąsce)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:BundleProductsUnderPriceNet", Label = "Łącz w zestawy poniżej X PLN", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:LogsExpirationDays", Label = "Ilość dni zachowania logów", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:FetchIntervalMinutes", Label = "Co ile wywoływać synchronizację (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:OfferProcessingDelayMinutes", Label = "Opóźnienie złożenia zamówienia (min)", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:NotificationsEmail", Label = "Adresy email do powiadomień (rodzielone średnikiem)", Group = "Ustawienia serwisu" },
            new ConfigField { Key = "AppSettings:AllegroDeliveryNames", Label = "Nazwy cenników dostawy Allegro", Group = "Ustawienia serwisu", FieldType = ConfigFieldType.StringList, Description = "Serwis obsługuje tylko zamówienia z tych cenników. Oferty wystawione ręcznie, spoza listy, są pomijane." },
            new ConfigField { Key = "AppSettings:DeliveryAddressEmail", Label = "Email podawany w adresie dostawy", Group = "Ustawienia serwisu" },

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