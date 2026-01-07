using AllegroErliProductsSyncService.Settings;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using System.Net.Http.Headers;
using System.Text;

namespace AllegroErliProductsSyncService.Services
{
    public class ErliClient
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerSettings _jsonSettings;

        public ErliClient(IOptions<ErliApiCredentials> options)
        {
            var credentials = options.Value;
            if (string.IsNullOrWhiteSpace(credentials.BaseUrl))
                throw new Exception("ErliBaseUrl is not configured.");
            if (string.IsNullOrWhiteSpace(credentials.ApiKey))
                throw new Exception("ErliApiKey is not configured.");

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(credentials.BaseUrl)
            };

            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ApiKey);

            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.Indented
            };
        }

        // Core reusable HTTP method with 429 retry
        private async Task<string> SendAsync(string endpoint, HttpMethod method, object body = null, int maxRetries = 5)
        {
            int attempt = 0;

            while (true)
            {
                attempt++;

                var request = new HttpRequestMessage(method, endpoint);
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body, _jsonSettings);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                }

                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                var response = await _httpClient.SendAsync(request);
                var responseBody = await response.Content.ReadAsStringAsync();
                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    return responseBody;
                }

                // Handle 429 (Too Many Requests)
                if ((int)response.StatusCode == 429 && attempt <= maxRetries)
                {
                    var delay = Math.Pow(2, attempt) * 500;
                    Log.Information("Erli API rate limit hit (429). Retrying after {Delay}ms. Attempt {Attempt}/{MaxRetries}", delay, attempt, maxRetries);
                    await Task.Delay((int)delay);
                    continue;
                }

                throw new HttpRequestException($"Erli API {method} failed: {response.StatusCode} ({response.ReasonPhrase})\n{responseBody}");
            }
        }

        // Generic typed helper
        private async Task<T> SendAsync<T>(string endpoint, HttpMethod method, object body = null)
        {
            var responseString = await SendAsync(endpoint, method, body);
            try
            {
                return JsonConvert.DeserializeObject<T>(responseString);
            }
            catch
            {
                //Log.Error(ex, "Failed to deserialize response for {Method} {Endpoint}. Body: {Body}", method, endpoint, responseString);
                throw;
            }
        }

        // Public API methods
        public Task<string> GetAsync(string endpoint) =>
            SendAsync(endpoint, HttpMethod.Get);

        public Task<string> PostAsync(string endpoint, object body) =>
            SendAsync(endpoint, HttpMethod.Post, body);

        public Task<string> PutAsync(string endpoint, object body) =>
            SendAsync(endpoint, HttpMethod.Put, body);

        public Task<string> PatchAsync(string endpoint, object body) =>
            SendAsync(endpoint, new HttpMethod("PATCH"), body);

        public Task<string> DeleteAsync(string endpoint) =>
            SendAsync(endpoint, HttpMethod.Delete);

        // Generic typed versions
        public Task<T> GetAsync<T>(string endpoint) =>
            SendAsync<T>(endpoint, HttpMethod.Get);

        public Task<T> PostAsync<T>(string endpoint, object body) =>
            SendAsync<T>(endpoint, HttpMethod.Post, body);

        public Task<T> PutAsync<T>(string endpoint, object body) =>
            SendAsync<T>(endpoint, HttpMethod.Put, body);

        public Task<T> PatchAsync<T>(string endpoint, object body) =>
            SendAsync<T>(endpoint, new HttpMethod("PATCH"), body);

        public Task<T> DeleteAsync<T>(string endpoint) =>
            SendAsync<T>(endpoint, HttpMethod.Delete);
    }
}