using ServiceManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ServiceManager.Helpers
{
    public static class ConfigFieldDefinitions
    {
        public static readonly List<ConfigField> AllFields = new()
            {
                // Gąska API
                new ConfigField { Key = "GaskaApiBaseUrl", Label = "Adres API Gąska", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiAcronym", Label = "Skrót Gąska", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiPerson", Label = "Osoba kontaktowa", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiPassword", Label = "Hasło", Group = "Gąska API", IsEnabled = false },
                new ConfigField { Key = "GaskaApiKey", Label = "Klucz API", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiProductsPerPage", Label = "Produkty na stronę", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiProductsInterval", Label = "Interwał pobierania produktów", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiProductPerDay", Label = "Produkty dziennie", Group = "Gąska API" , IsEnabled = false},
                new ConfigField { Key = "GaskaApiProductInterval", Label = "Interwał produktów", Group = "Gąska API", IsEnabled = false},

                // Allegro API
                new ConfigField { Key = "AllegroApiBaseUrl", Label = "Adres API Allegro", Group = "Allegro API" , IsEnabled = false},
                new ConfigField { Key = "AllegroAuthBaseUrl", Label = "Adres Autoryzacji Allegro", Group = "Allegro API" , IsEnabled = false},
                new ConfigField { Key = "AllegroClientName", Label = "Nazwa klienta", Group = "Allegro API", IsEnabled = false},
                new ConfigField { Key = "AllegroClientId", Label = "Client ID", Group = "Allegro API", IsEnabled = false},
                new ConfigField { Key = "AllegroClientSecret", Label = "Client Secret", Group = "Allegro API", IsEnabled = false },
                new ConfigField { Key = "AllegroScope", Label = "Zakres uprawnień", Group = "Allegro API" , IsEnabled = false},

                // ERLI API
                new ConfigField { Key = "ErliBaseUrl", Label = "Adres API Erli", Group = "Erli API" , IsEnabled = false},
                new ConfigField { Key = "ErliApiKey", Label = "API Key Erli", Group = "Erli API" , IsEnabled = false},

                // Other settings
                new ConfigField { Key = "Categories", Label = "ID synchronizowanych kategorii", Group = "Inne ustawienia" },
                new ConfigField { Key = "LogsExpirationDays", Label = "Ilość dni zachowania logów", Group = "Inne ustawienia" },
                new ConfigField { Key = "FetchIntervalMinutes", Label = "Odświeżanie stanu/ceny (min)", Group = "Inne ustawienia" },
                new ConfigField { Key = "OwnMarginPercent", Label = "Własna marża (%)", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroMarginUnder5PLN", Label = "Marża allegro poniżej 5 PLN", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroMarginBetween5and1000PLNPercent", Label = "Marża allegro 5-1000 PLN (%)", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroMarginMoreThan1000PLN", Label = "Marża allegro powyżej 1000 PLN", Group = "Inne ustawienia" },
                new ConfigField { Key = "AddPLNToBulkyProducts", Label = "Dodatek PLN do towarów gabarytowych", Group = "Inne ustawienia" },
                new ConfigField { Key = "AddPLNToCustomProducts", Label = "Dodatek PLN do towarów niestandardowych", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroDeliveryName", Label = "Nazwa cennika dostawy Allegro", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroHandlingTime", Label = "Czas realizacji", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroHandlingTimeCustomProducts", Label = "Czas realizacji produktów niestandardowych", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroSafetyMeasures", Label = "Tekst bezpieczeństwa", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroWarranty", Label = "Nazwa polityki gwarancji", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroReturnPolicy", Label = "Nazwa polityki zwrotów", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroImpliedWarranty", Label = "Nazwa polityki reklamacji", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroResponsiblePerson", Label = "Odpowiedzialna osoba", Group = "Inne ustawienia" },
                new ConfigField { Key = "AllegroResponsibleProducer", Label = "Odpowiedzialny producent", Group = "Inne ustawienia" },
            };
    }
}