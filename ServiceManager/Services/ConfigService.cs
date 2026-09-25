using Microsoft.Extensions.Configuration;
using ServiceManager.Enums;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ServiceManager.Services
{
    public class ConfigService
    {
        public IConfigurationRoot LoadAppSettings(string path)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Path.GetDirectoryName(path) ?? ".")
                .AddJsonFile(Path.GetFileName(path), optional: false, reloadOnChange: true);

            return builder.Build();
        }

        /// <summary>
        /// Sprawdza obecność klucza bezpośrednio w pliku. IConfiguration nie widzi pustych tablic,
        /// a pusta lista kategorii to poprawna konfiguracja, którą trzeba pokazać w konfiguratorze.
        /// </summary>
        public bool KeyExists(string path, string key)
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(path));

            foreach (var part in key.Split(':'))
            {
                if (node is not JsonObject obj || !obj.TryGetPropertyValue(part, out node) || node is null)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Zapis przez plik tymczasowy. To jest appsettings pracującej usługi - przerwany
        /// zapis w miejscu zostawiłby uszkodzony plik i usługa nie wstałaby po restarcie.
        /// </summary>
        private static void WriteAtomic(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            var temporaryPath = path + ".tmp";

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(path))
            {
                var backupPath = path + ".bak";
                File.Replace(temporaryPath, path, backupPath, ignoreMetadataErrors: true);
                return;
            }

            File.Move(temporaryPath, path);
        }

        /// <summary>
        /// Zapisuje wartości z ekranu do appsettings. <paramref name="fieldTypes"/> decyduje,
        /// czy pole ma trafić do pliku jako liczba, czy jako tekst - liczba zapisana w cudzysłowie
        /// działa, ale plik przestaje być czytelny i rozjeżdża się z typami w klasach ustawień.
        /// </summary>
        public void SaveAppSettings(string path, Dictionary<string, string> values, IReadOnlyDictionary<string, ConfigFieldType>? fieldTypes = null)
        {
            var json = File.ReadAllText(path);
            var jsonObject = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

            foreach (var kvp in values)
            {
                var parts = kvp.Key.Split(':');
                JsonObject current = jsonObject;

                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (current[parts[i]] is not JsonObject)
                        current[parts[i]] = new JsonObject();

                    current = current[parts[i]]!.AsObject();
                }

                var previous = current[parts[^1]];
                current.Remove(parts[^1]);

                if (kvp.Value.StartsWith("{") || kvp.Value.StartsWith("["))
                {
                    current[parts[^1]] = JsonNode.Parse(kvp.Value);
                    continue;
                }

                ConfigFieldType? fieldType = fieldTypes != null && fieldTypes.TryGetValue(kvp.Key, out var declared)
                    ? declared
                    : null;

                current[parts[^1]] = ToJsonValue(kvp.Value, fieldType, previous);
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            WriteAtomic(path, jsonObject.ToJsonString(options));
        }

        /// <summary>
        /// Wartość pola jako węzeł JSON. Typ bierzemy z definicji pola, a gdy jej nie ma -
        /// z tego, co było w pliku, żeby zapis nie zmieniał liczby w tekst.
        /// </summary>
        private static JsonNode? ToJsonValue(string value, ConfigFieldType? fieldType, JsonNode? previous)
        {
            var numeric = fieldType switch
            {
                ConfigFieldType.Int or ConfigFieldType.Decimal => true,
                null or ConfigFieldType.String => previous is JsonValue prev && prev.GetValueKind() == JsonValueKind.Number,
                _ => false
            };

            if (!numeric || string.IsNullOrWhiteSpace(value))
                return JsonValue.Create(value);

            if (fieldType == ConfigFieldType.Int && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return JsonValue.Create(integer);

            if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                return JsonValue.Create(number);

            // Nie parsuje się na liczbę - walidacja to zgłosi, a wartości użytkownika nie gubimy.
            return JsonValue.Create(value);
        }
    }
}
