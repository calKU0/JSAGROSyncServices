using AllegroGaskaOrdersSyncService.DTOs.AllegroApi;
using AllegroGaskaOrdersSyncService.Settings;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AllegroGaskaOrdersSyncService.Services
{
    public class AllegroApiClient
    {
        private readonly ILogger<AllegroApiClient> _logger;
        private readonly AllegroAuthService _auth;
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _options;
        private const int MaxRetries = 3;
        private const int DelayOnTooManyRequestsMs = 30_000;

        public AllegroApiClient(ILogger<AllegroApiClient> logger, IOptions<AllegroApiCredentials> credentials, AllegroAuthService authService, HttpClient httpClient)
        {
            _logger = logger;
            _auth = authService;
            _http = httpClient;
            _http.BaseAddress = new Uri(credentials.Value.BaseUrl);

            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        public async Task<T> GetAsync<T>(string url, CancellationToken ct)
        {
            return await DeserializeWithRetry<T>(
                () => CreateRequest(HttpMethod.Get, url, ct).ContinueWith(t => _http.SendAsync(t.Result, ct)).Unwrap()
            );
        }

        public async Task<ApiResult<T>> PostAsyncWithResult<T>(string url, object body, CancellationToken ct, string contentType = "application/vnd.allegro.public.v1+json")
        {
            HttpResponseMessage response = null!;
            string bodyString = string.Empty;

            try
            {
                response = await SendWithResponseAsync(url, HttpMethod.Post, body, ct);
                bodyString = await response.Content.ReadAsStringAsync();

                T? data = default;
                if (response.IsSuccessStatusCode)
                {
                    data = JsonSerializer.Deserialize<T>(bodyString, _options);
                }

                return new ApiResult<T>
                {
                    Data = data,
                    Response = response,
                    Body = bodyString
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception while calling Allegro API for {Url}", url);
                throw;
            }
        }

        public async Task<HttpResponseMessage> SendWithResponseAsync(string url, HttpMethod method, object body = null, CancellationToken ct = default)
        {
            var request = await CreateRequest(method, url, ct);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, _options);
                request.Content = new StringContent(json, Encoding.UTF8, "application/vnd.allegro.public.v1+json");
            }

            return await _http.SendAsync(request, ct);
        }

        private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string url, CancellationToken ct)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
            return request;
        }

        private async Task<T> DeserializeWithRetry<T>(Func<Task<HttpResponseMessage>> send)
        {
            int retryCount = 0;

            while (true)
            {
                var response = await send();
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<T>(body, _options);
                }

                if ((int)response.StatusCode == 429 && retryCount < MaxRetries)
                {
                    _logger.LogWarning("Rate limit exceeded. Waiting 30 seconds before retry {RetryCount}/{MaxRetries}...",
                        retryCount + 1, MaxRetries);
                    await Task.Delay(DelayOnTooManyRequestsMs);

                    retryCount++;
                    continue;
                }
                if ((int)response.StatusCode == 404)
                {
                    return default;
                }

                _logger.LogError("Allegro API error {Status}: {Body}", response.StatusCode, body);
                return default;
            }
        }
    }
}