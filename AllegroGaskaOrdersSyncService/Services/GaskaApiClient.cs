using AllegroGaskaOrdersSyncService.Settings;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AllegroGaskaOrdersSyncService.Services
{
    public class GaskaApiClient
    {
        private readonly ILogger<GaskaApiClient> _logger;
        private readonly HttpClient _http;
        private readonly GaskaApiCredentials _credentials;

        public GaskaApiClient(ILogger<GaskaApiClient> logger, HttpClient httpClient, IOptions<GaskaApiCredentials> credentials)
        {
            _logger = logger;
            _http = httpClient;
            _credentials = credentials.Value;
        }

        public async Task<T> GetAsync<T>(string url, CancellationToken ct)
        {
            return await Deserialize<T>(() => CreateRequest(HttpMethod.Get, url, ct)
                .ContinueWith(t => _http.SendAsync(t.Result, ct)).Unwrap());
        }

        public async Task<T> PostAsync<T>(string url, object body, CancellationToken ct, string contentType = "application/json")
        {
            return await Deserialize<T>(async () =>
            {
                var request = await CreateRequest(HttpMethod.Post, url, ct);

                if (body != null)
                {
                    if (body is byte[] bytes)
                    {
                        request.Content = new ByteArrayContent(bytes);
                        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    }
                    else
                    {
                        var json = JsonSerializer.Serialize(body);
                        request.Content = new StringContent(json, Encoding.UTF8, contentType);
                    }
                }

                return await _http.SendAsync(request, ct);
            });
        }

        public async Task<HttpResponseMessage> SendWithResponseAsync(string url, HttpMethod method, object body = null, CancellationToken ct = default)
        {
            var request = await CreateRequest(method, url, ct);

            if (body != null)
            {
                var json = JsonSerializer.Serialize(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            return await _http.SendAsync(request, ct);
        }

        private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string url, CancellationToken ct)
        {
            var request = new HttpRequestMessage(method, new Uri(new Uri(_credentials.BaseUrl), url));

            // Basic Auth
            var username = $"{_credentials.Acronym}|{_credentials.Person}";
            var password = _credentials.Password;
            var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", authValue);

            // X-Signature header
            var signature = GenerateSignature();
            request.Headers.Add("X-Signature", signature);

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            return await Task.FromResult(request);
        }

        private string GenerateSignature()
        {
            var input = $"acronym={_credentials.Acronym}&person={_credentials.Person}&password={_credentials.Password}&key={_credentials.Key}";
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<T> Deserialize<T>(Func<Task<HttpResponseMessage>> send)
        {
            var response = await send();
            var body = await response.Content.ReadAsStringAsync();

            try
            {
                var result = JsonSerializer.Deserialize<T>(body);
                return result;
            }
            catch (JsonException)
            {
                throw new HttpRequestException($"Failed to deserialize response with status {(int)response.StatusCode} ({response.StatusCode}). Body: {body}");
            }
        }
    }
}