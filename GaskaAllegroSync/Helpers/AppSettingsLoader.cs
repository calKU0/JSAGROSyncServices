using GaskaAllegroSync.DTOs;
using GaskaAllegroSync.DTOs.Settings;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;

namespace GaskaAllegroSync.Helpers
{
    public static class AppSettingsLoader
    {
        public static GaskaApiCredentials LoadGaskaCredentials()
        {
            return new GaskaApiCredentials
            {
                BaseUrl = GetString("GaskaApiBaseUrl"),
                Acronym = GetString("GaskaApiAcronym"),
                Person = GetString("GaskaApiPerson"),
                Password = GetString("GaskaApiPassword"),
                ApiKey = GetString("GaskaApiKey"),
                ProductsPerPage = GetInt("GaskaApiProductsPerPage", 1000),
                ProductsInterval = GetInt("GaskaApiProductsInterval", 1),
                ProductPerDay = GetInt("GaskaApiProductPerDay", 500),
                ProductInterval = GetInt("GaskaApiProductInterval", 10)
            };
        }

        public static AllegroApiCredentials LoadAllegroCredentials()
        {
            return new AllegroApiCredentials
            {
                BaseUrl = GetString("AllegroApiBaseUrl"),
                AuthBaseUrl = GetString("AllegroAuthBaseUrl"),
                ClientName = GetString("AllegroClientName"),
                ClientId = GetString("AllegroClientId"),
                ClientSecret = GetString("AllegroClientSecret"),
                Scope = GetString("AllegroScope"),
            };
        }

        public static AppSettings LoadAppSettings()
        {
            decimal ParseDecimal(string key, decimal defaultValue = 0)
            {
                string raw = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrWhiteSpace(raw))
                    return defaultValue;

                if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val))
                    return val;

                return defaultValue;
            }

            int ParseInt(string key, int defaultValue)
            {
                string raw = ConfigurationManager.AppSettings[key];
                if (int.TryParse(raw, out int val))
                    return val;

                return defaultValue;
            }

            List<int> ParseIntList(string key, char separator = ',', bool required = true)
            {
                string raw = ConfigurationManager.AppSettings[key];

                if (required && string.IsNullOrWhiteSpace(raw))
                    return new List<int>();

                return raw?.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s =>
                           {
                               if (int.TryParse(s.Trim(), out int val))
                                   return val;
                               return 0; // default for invalid integers
                           })
                           .Where(v => v != 0)
                           .ToList() ?? new List<int>();
            }

            return new AppSettings
            {
                CategoriesId = ParseIntList("Categories"),

                LogsExpirationDays = ParseInt("LogsExpirationDays", 14),
                FetchIntervalMinutes = ParseInt("FetchIntervalMinutes", 120),

                OwnMarginPercent = ParseDecimal("OwnMarginPercent"),
                AllegroMarginUnder5PLN = ParseDecimal("AllegroMarginUnder5PLN"),
                AllegroMarginBetween5and1000PLNPercent = ParseDecimal("AllegroMarginBetween5and1000PLNPercent"),
                AllegroMarginMoreThan1000PLN = ParseDecimal("AllegroMarginMoreThan1000PLN"),

                AddPLNToBulkyProducts = ParseDecimal("AddPLNToBulkyProducts"),
                AddPLNToCustomProducts = ParseDecimal("AddPLNToCustomProducts"),

                AllegroDeliveryName = GetString("AllegroDeliveryName"),
                AllegroHandlingTime = GetString("AllegroHandlingTime"),
                AllegroHandlingTimeCustomProducts = GetString("AllegroHandlingTimeCustomProducts"),
                AllegroSafetyMeasures = GetString("AllegroSafetyMeasures"),
                AllegroWarranty = GetString("AllegroWarranty"),
                AllegroReturnPolicy = GetString("AllegroReturnPolicy"),
                AllegroImpliedWarranty = GetString("AllegroImpliedWarranty"),
                AllegroResponsiblePerson = GetString("AllegroResponsiblePerson"),
                AllegroResponsibleProducer = GetString("AllegroResponsibleProducer")
            };
        }

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
    }
}