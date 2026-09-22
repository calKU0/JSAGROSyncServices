using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
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

        public void SaveAppSettings(string path, Dictionary<string, string> values)
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

                current.Remove(parts[^1]);

                if (kvp.Value.StartsWith("{") || kvp.Value.StartsWith("["))
                {
                    current[parts[^1]] = JsonNode.Parse(kvp.Value);
                }
                else
                {
                    current[parts[^1]] = JsonValue.Create(kvp.Value);
                }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            WriteAtomic(path, jsonObject.ToJsonString(options));
        }
    }
}
