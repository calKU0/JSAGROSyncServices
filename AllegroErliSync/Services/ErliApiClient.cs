using AllegroErliSync.Helpers;
using AllegroErliSync.Settings;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AllegroErliSync.Services
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;

    // ...

    public class ErliClient
    {
        private readonly HttpClient _httpClient;
        private readonly ErliApiCredentials _erliApiCredentials;
        private readonly JsonSerializerSettings _jsonSettings;

        public ErliClient()
        {
            _erliApiCredentials = AppSettingsLoader.LoadErliCredentials();

            if (string.IsNullOrWhiteSpace(_erliApiCredentials.BaseUrl))
                throw new Exception("ErliBaseUrl is not configured.");
            if (string.IsNullOrWhiteSpace(_erliApiCredentials.ApiKey))
                throw new Exception("ErliApiKey is not configured.");

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_erliApiCredentials.BaseUrl)
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _erliApiCredentials.ApiKey);

            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            // JSON settings: camelCase + ignore nulls
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
        }

        /// <summary>
        /// Sends a POST request to the specified endpoint with a JSON body.
        /// </summary>
        public async Task<string> PostAsync(string endpoint, object body)
        {
            // Serialize with JSON settings
            var json = JsonConvert.SerializeObject(body, _jsonSettings);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // ✅ Log the full request body
            Log.Information("📦 Sending request to Erli endpoint {Endpoint}:\n{RequestBody}", endpoint, json);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var response = await _httpClient.PostAsync(endpoint, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();

            Log.Information("[ErliAPI] Response {StatusCode} from {Endpoint} in {Elapsed} ms. Body: {ResponseBody}", response.StatusCode, endpoint, stopwatch.ElapsedMilliseconds, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                Log.Error("[ErliAPI] Request failed: {StatusCode} {ReasonPhrase}. Response: {ResponseBody}", response.StatusCode, response.ReasonPhrase, responseBody);

                throw new Exception($"Erli API call failed: {response.StatusCode} ({response.ReasonPhrase})\n{responseBody}");
            }

            return responseBody;
        }

        /// <summary>
        /// Generic POST that deserializes the response to T.
        /// </summary>
        public async Task<T> PostAsync<T>(string endpoint, object body)
        {
            var responseString = await PostAsync(endpoint, body);

            try
            {
                return JsonConvert.DeserializeObject<T>(responseString);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize Erli response for {Endpoint}. Body: {Body}", endpoint, responseString);
                throw;
            }
        }
    }
}