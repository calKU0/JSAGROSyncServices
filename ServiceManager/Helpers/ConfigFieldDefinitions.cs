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

            // AppSettings
            new ConfigField { Key = "AppSettings:CategoriesId", Label = "ID synchronizowanych kategorii", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:MinProductStock", Label = "Minimalny stan produktu", Group = "Inne ustawienia", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:LogsExpirationDays", Label = "Ilość dni zachowania logów", Group = "Inne ustawienia", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:FetchIntervalMinutes", Label = "Co ile wywoływać synchronizację (min)", Group = "Inne ustawienia", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:OwnMarginPercent", Label = "Własna marża (%)", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:OwnMarginPercentUnder10PLN", Label = "Własna marża poniżej 10 PLN (%)", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AllegroMarginUnder5PLN", Label = "Marża allegro poniżej 5 PLN", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AllegroMarginBetween5and1000PLNPercent", Label = "Marża allegro 5-1000 PLN (%)", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AllegroMarginMoreThan1000PLN", Label = "Marża allegro powyżej 1000 PLN", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AddPLNToBulkyProducts", Label = "Dodatek PLN do towarów gabarytowych", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AddPLNToCustomProducts", Label = "Dodatek PLN do towarów niestandardowych", Group = "Inne ustawienia", FieldType = ConfigFieldType.Decimal },
            new ConfigField { Key = "AppSettings:AllegroDeliveryName", Label = "Nazwa cennika dostawy Allegro", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroHandlingTime", Label = "Czas realizacji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroHandlingTimeCustomProducts", Label = "Czas realizacji produktów niestandardowych", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroSafetyMeasures", Label = "Tekst bezpieczeństwa", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroWarranty", Label = "Nazwa polityki gwarancji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroReturnPolicy", Label = "Nazwa polityki zwrotów", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroImpliedWarranty", Label = "Nazwa polityki reklamacji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroResponsiblePerson", Label = "Odpowiedzialna osoba", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroResponsibleProducer", Label = "Odpowiedzialny producent", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:OfferProcessingDelayMinutes", Label = "Opóźnienie złożenia zamówienia (min)", Group = "Inne ustawienia", FieldType = ConfigFieldType.Int },
            new ConfigField { Key = "AppSettings:NotificationsEmail", Label = "Adresy email do powiadomień (rodzielone średnikiem)", Group = "Inne ustawienia" },
        };
    }
}