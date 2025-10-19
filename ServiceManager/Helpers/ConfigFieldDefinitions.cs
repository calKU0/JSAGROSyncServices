using ServiceManager.Models;

namespace ServiceManager.Helpers
{
    public static class ConfigFieldDefinitions
    {
        public static readonly List<ConfigField> AllFields = new()
        {
            // Gąska API
            new ConfigField { Key = "GaskaApi:BaseUrl", Label = "Adres API Gąska", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:Acronym", Label = "Skrót Gąska", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:Person", Label = "Osoba kontaktowa", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:Password", Label = "Hasło", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:Key", Label = "Klucz API", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:ProductsPerPage", Label = "Produkty na stronę", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:ProductsInterval", Label = "Interwał pobierania produktów", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:ProductPerDay", Label = "Produkty dziennie", Group = "Gąska API", IsEnabled = false },
            new ConfigField { Key = "GaskaApi:ProductInterval", Label = "Interwał produktów", Group = "Gąska API", IsEnabled = false },

            // Allegro API
            new ConfigField { Key = "AllegroApi:BaseUrl", Label = "Adres API Allegro", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApi:AuthBaseUrl", Label = "Adres Autoryzacji Allegro", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApi:ClientName", Label = "Nazwa klienta", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApi:ClientId", Label = "Client ID", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApi:ClientSecret", Label = "Client Secret", Group = "Allegro API", IsEnabled = false },
            new ConfigField { Key = "AllegroApi:Scope", Label = "Zakres uprawnień", Group = "Allegro API", IsEnabled = false },

            // ERLI API
            new ConfigField { Key = "ErliApi:BaseUrl", Label = "Adres API Erli", Group = "Erli API", IsEnabled = false },
            new ConfigField { Key = "ErliApi:ApiKey", Label = "API Key Erli", Group = "Erli API", IsEnabled = false },

            // AppSettings
            new ConfigField { Key = "AppSettings:CategoriesId", Label = "ID synchronizowanych kategorii", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:LogsExpirationDays", Label = "Ilość dni zachowania logów", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:FetchIntervalMinutes", Label = "Odświeżanie stanu/ceny (min)", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:OwnMarginPercent", Label = "Własna marża (%)", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroMarginUnder5PLN", Label = "Marża allegro poniżej 5 PLN", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroMarginBetween5and1000PLNPercent", Label = "Marża allegro 5-1000 PLN (%)", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroMarginMoreThan1000PLN", Label = "Marża allegro powyżej 1000 PLN", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AddPLNToBulkyProducts", Label = "Dodatek PLN do towarów gabarytowych", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AddPLNToCustomProducts", Label = "Dodatek PLN do towarów niestandardowych", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroDeliveryName", Label = "Nazwa cennika dostawy Allegro", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroHandlingTime", Label = "Czas realizacji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroHandlingTimeCustomProducts", Label = "Czas realizacji produktów niestandardowych", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroSafetyMeasures", Label = "Tekst bezpieczeństwa", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroWarranty", Label = "Nazwa polityki gwarancji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroReturnPolicy", Label = "Nazwa polityki zwrotów", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroImpliedWarranty", Label = "Nazwa polityki reklamacji", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroResponsiblePerson", Label = "Odpowiedzialna osoba", Group = "Inne ustawienia" },
            new ConfigField { Key = "AppSettings:AllegroResponsibleProducer", Label = "Odpowiedzialny producent", Group = "Inne ustawienia" },
        };
    }
}