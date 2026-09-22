using System.Net;
using System.Text;
using System.Text.Json;

namespace JSAGROSyncServices.Orders.Services
{
    /// <summary>
    /// Klient API Gąski używany przy zamówieniach.
    /// Odczyty są ponawiane przy błędach przejściowych; zapisy nigdy - powtórzony POST
    /// złożyłby zamówienie drugi raz.
    /// </summary>
    public class GaskaOrdersApiClient
    {
        private const int MaxReadRetries = 3;
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

        private readonly ILogger<GaskaOrdersApiClient> _logger;
        private readonly HttpClient _http;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public GaskaOrdersApiClient(ILogger<GaskaOrdersApiClient> logger, HttpClient http)
        {
            _logger = logger;
            _http = http;
        }

        public async Task<T> GetAsync<T>(string url, CancellationToken ct = default)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var response = await _http.GetAsync(url, ct);
                    return await ReadAsync<T>(response, HttpMethod.Get, url, ct);
                }
                catch (Exception ex) when (attempt < MaxReadRetries && IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    _logger.LogWarning("Gąska API GET {Url} failed ({Reason}). Retry {Attempt}/{Max}.",
                        url, ex.Message, attempt, MaxReadRetries);

                    await Task.Delay(RetryDelay, ct);
                }
            }
        }

        /// <summary>
        /// Zapis - bez ponawiania. Wywołanie, które nie zdążyło odpowiedzieć, mogło mimo to
        /// dojść do Gąski, więc decyzję o powtórzeniu podejmuje kod wywołujący.
        /// </summary>
        public async Task<T> PostAsync<T>(string url, object body, CancellationToken ct = default)
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.PostAsync(url, content, ct);
            return await ReadAsync<T>(response, HttpMethod.Post, url, ct);
        }

        private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpMethod method, string url, CancellationToken ct)
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            T? result;

            try
            {
                result = JsonSerializer.Deserialize<T>(body, JsonOptions);
            }
            catch (JsonException)
            {
                throw new HttpRequestException(
                    $"Gąska API {method} {url} zwróciła odpowiedź, której nie da się odczytać ({(int)response.StatusCode} {response.StatusCode}). Treść: {Trim(body)}");
            }

            if (result == null)
            {
                throw new HttpRequestException(
                    $"Gąska API {method} {url} zwróciła pustą odpowiedź ({(int)response.StatusCode} {response.StatusCode}). Treść: {Trim(body)}");
            }

            return result;
        }

        private static bool IsTransient(Exception ex) => ex switch
        {
            TaskCanceledException => true,
            TimeoutException => true,
            HttpRequestException http => http.StatusCode is null
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError
                or HttpStatusCode.BadGateway
                or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout,
            _ => false
        };

        private static string Trim(string body) => body.Length <= 500 ? body : body[..500] + "...";
    }
}
