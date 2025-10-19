using AllegroErliSync.Settings;
using System.Configuration;

namespace AllegroErliSync.Helpers
{
    public static class AppSettingsLoader
    {
        public static ErliApiCredentials LoadErliCredentials()
        {
            return new ErliApiCredentials
            {
                BaseUrl = GetString("ErliBaseUrl"),
                ApiKey = GetString("ErliApiKey"),
            };
        }

        public static AppSettings LoadAppSettings()
        {
            int ParseInt(string key, int defaultValue)
            {
                string raw = ConfigurationManager.AppSettings[key];
                if (int.TryParse(raw, out int val))
                    return val;

                return defaultValue;
            }

            return new AppSettings
            {
                LogsExpirationDays = ParseInt("LogsExpirationDays", 14),
                FetchIntervalMinutes = ParseInt("FetchIntervalMinutes", 120),
            };
        }

        private static string GetString(string key, bool required = true)
        {
            var value = ConfigurationManager.AppSettings[key];

            if (required && string.IsNullOrWhiteSpace(value))
                throw new ConfigurationErrorsException($"Missing required appSetting: '{key}'");

            return value;
        }
    }
}