using JSAGROAllegroSync.DTOs;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;

namespace JSAGROAllegroSync.Helpers
{
    public static class AppSettingsLoader
    {
        public static GaskaApiSettings LoadApiSettings()
        {
            return new GaskaApiSettings
            {
                BaseUrl = GetString("GaskaApiBaseUrl"),
                Acronym = GetString("GaskaApiAcronym"),
                Person = GetString("GaskaApiPerson"),
                Password = GetString("GaskaApiPassword"),
                ApiKey = GetString("GaskaApiKey"),
                CategoriesId = GetIntList("GaskaApiCategoriesId"),
                ProductsPerPage = GetInt("GaskaApiProductsPerPage", 1000),
                ProductsInterval = GetInt("GaskaApiProductsInterval", 1),
                ProductPerDay = GetInt("GaskaApiProductPerDay", 500),
                ProductInterval = GetInt("GaskaApiProductInterval", 10)
            };
        }

        public static AllegroApiSettings LoadFtpSettings()
        {
            return new AllegroApiSettings
            {
                BaseUrl = GetString("AllegroApiBaseUrl"),
                ClientName = GetString("AllegroClientName"),
                ClientId = GetString("AllegroClientId"),
                ClientSecret = GetString("AllegroClientSecret"),
                Scope = GetString("AllegroScope"),
            };
        }

        public static int GetLogsExpirationDays() => GetInt("LogsExpirationDays", 14);

        public static TimeSpan GetFetchInterval() => TimeSpan.FromMinutes(GetInt("FetchIntervalMinutes", 60));

        private static string GetString(string key, bool required = true)
        {
            var value = ConfigurationManager.AppSettings[key];

            if (required && string.IsNullOrWhiteSpace(value))
                throw new ConfigurationErrorsException($"Missing required appSetting: '{key}'");

            return value;
        }

        private static int GetInt(string key, int defaultValue)
        {
            var raw = ConfigurationManager.AppSettings[key];
            if (int.TryParse(raw, out int result))
                return result;

            return defaultValue;
        }

        private static List<int> GetIntList(string key, char separator = ',', bool required = true)
        {
            var raw = ConfigurationManager.AppSettings[key];

            if (required && string.IsNullOrWhiteSpace(raw))
                throw new ConfigurationErrorsException($"Missing required appSetting: '{key}'");

            return raw?.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(s =>
                       {
                           if (int.TryParse(s.Trim(), out int val))
                               return val;
                           throw new ConfigurationErrorsException($"Invalid integer in '{key}': '{s}'");
                       })
                       .ToList() ?? new List<int>();
        }
    }
}