using JSAGROSyncServices.Infrastructure.Services;
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

                    if (!response.IsSuccessStatusCode
                        && attempt < HttpRetryPolicy.MaxAttempts
                        && HttpRetryPolicy.ShouldRetry(response.StatusCode))
                    {
                        var delay = HttpRetryPolicy.GetDelay(response, attempt);

                        HttpRetryPolicy.LogRetry(_logger, "Gąska", HttpMethod.Get, url, response.StatusCode, attempt, delay);

                        await Task.Delay(delay, ct);
                        continue;
                    }

                    return await ReadAsync<T>(response, HttpMethod.Get, url, ct);
                }
                catch (Exception ex) when (attempt < HttpRetryPolicy.MaxAttempts && IsTransient(ex) && !ct.IsCancellationRequested)
                {
                    // Zerwane połączenie albo przekroczony czas - odpowiedzi nie ma, więc powtórzenie odczytu jest bezpieczne.
                    var delay = HttpRetryPolicy.GetDelay(attempt);

                    _logger.LogWarning(
                        "Gąska GET {Url} failed ({Reason}). Retry {Attempt}/{MaxAttempts} in {Delay}.",
                        url, ex.Message, attempt, HttpRetryPolicy.MaxAttempts, delay);

                    await Task.Delay(delay, ct);
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

        /// <summary>Wyjątki połączeniowe - odpowiedź nie dotarła, więc odczyt można powtórzyć.</summary>
        private static bool IsTransient(Exception ex) => ex switch
        {
            TaskCanceledException => true,
            TimeoutException => true,
            HttpRequestException http => http.StatusCode is null || HttpRetryPolicy.ShouldRetry(http.StatusCode.Value),
            _ => false
        };

        private static string Trim(string body) => body.Length <= 500 ? body : body[..500] + "...";
    }
}
