using JSAGROSyncServices.Infrastructure.Services;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.Services
{
    /// <summary>
    /// Klient API Erli. Ponawia wywołania według wspólnej polityki (<see cref="HttpRetryPolicy"/>),
    /// a nieudane odpowiedzi zamienia na <see cref="ErliApiException"/> z odczytaną treścią błędu.
    /// </summary>
    public class ErliClient
    {
        private static readonly HttpMethod Patch = new("PATCH");

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ILogger<ErliClient> _logger;
        private readonly HttpClient _http;

        public ErliClient(ILogger<ErliClient> logger, HttpClient http)
        {
            _logger = logger;
            _http = http;
        }

        public Task<T> GetAsync<T>(string endpoint, CancellationToken ct = default) =>
            SendAsync<T>(endpoint, HttpMethod.Get, null, ct);

        public Task<T> PostAsync<T>(string endpoint, object? body, CancellationToken ct = default) =>
            SendAsync<T>(endpoint, HttpMethod.Post, body, ct);

        public Task<T> PatchAsync<T>(string endpoint, object? body, CancellationToken ct = default) =>
            SendAsync<T>(endpoint, Patch, body, ct);

        public Task PostAsync(string endpoint, object? body, CancellationToken ct = default) =>
            SendAsync(endpoint, HttpMethod.Post, body, ct);

        public Task PatchAsync(string endpoint, object? body, CancellationToken ct = default) =>
            SendAsync(endpoint, Patch, body, ct);

        private async Task<T> SendAsync<T>(string endpoint, HttpMethod method, object? body, CancellationToken ct)
        {
            var responseBody = await SendAsync(endpoint, method, body, ct);

            try
            {
                return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
                    ?? throw new ErliApiException(method, endpoint, HttpStatusCode.OK, responseBody, null);
            }
            catch (JsonException ex)
            {
                throw new ErliApiException(method, endpoint, HttpStatusCode.OK,
                    $"Nie udało się odczytać odpowiedzi: {ex.Message}. Treść: {responseBody}", null);
            }
        }

        private async Task<string> SendAsync(string endpoint, HttpMethod method, object? body, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, endpoint);

                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body, JsonOptions),
                        Encoding.UTF8,
                        "application/json");
                }

                using var response = await _http.SendAsync(request, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                    return responseBody;

                if (attempt < HttpRetryPolicy.MaxAttempts && HttpRetryPolicy.ShouldRetry(response.StatusCode))
                {
                    var delay = HttpRetryPolicy.GetDelay(response, attempt);

                    HttpRetryPolicy.LogRetry(_logger, "Erli", method, endpoint, response.StatusCode, attempt, delay);

                    await Task.Delay(delay, ct);
                    continue;
                }

                throw new ErliApiException(method, endpoint, response.StatusCode, responseBody, TryParseError(responseBody));
            }
        }

        private static ErliApiError? TryParseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ErliApiError>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
