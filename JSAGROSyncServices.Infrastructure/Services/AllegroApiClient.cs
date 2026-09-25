using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Infrastructure.Services
{
    public class AllegroApiClient
    {
        private readonly ILogger<AllegroApiClient> _logger;
        private readonly AllegroAuthService _auth;
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _options;

        public AllegroApiClient(ILogger<AllegroApiClient> logger, AllegroAuthService authService, HttpClient httpClient)
        {
            _logger = logger;
            _auth = authService;
            _http = httpClient;

            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        public async Task<T?> GetAsync<T>(string url, CancellationToken ct)
        {
            return await DeserializeWithRetry<T>(async () =>
            {
                var request = await CreateRequest(HttpMethod.Get, url, ct);
                return await _http.SendAsync(request, ct);
            }, url);
        }

        public async Task<T?> PostAsync<T>(string url, object body, CancellationToken ct, string contentType = "application/vnd.allegro.public.v1+json")
        {
            return await DeserializeWithRetry<T>(async () =>
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
                        var json = JsonSerializer.Serialize(body, _options);
                        request.Content = new StringContent(json, Encoding.UTF8, contentType);
                    }
                }

                return await _http.SendAsync(request, ct);
            }, url);
        }

        public async Task<HttpResponseMessage> SendWithResponseAsync(
            string url,
            HttpMethod method,
            object? body = null,
            CancellationToken ct = default)
        {
            return await SendWithRetry(async () =>
            {
                var request = await CreateRequest(method, url, ct);

                if (body != null)
                {
                    var json = JsonSerializer.Serialize(body, _options);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/vnd.allegro.public.v1+json");
                }

                return await _http.SendAsync(request, ct);
            });
        }

        private async Task<HttpRequestMessage> CreateRequest(HttpMethod method, string url, CancellationToken ct)
        {
            var token = await _auth.GetAccessTokenAsync(ct);
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
            return request;
        }

        /// <summary>
        /// Zwraca default przy bledzie - wywolujacy sprawdza wynik na null.
        /// Powod bledu logujemy tutaj, inaczej wyzej widac tylko "brak danych".
        /// </summary>
        private async Task<T?> DeserializeWithRetry<T>(Func<Task<HttpResponseMessage>> send, string url)
        {
            var response = await SendWithRetry(send);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // 404 to normalna odpowiedz przy wyszukiwaniu - nie zasmiecamy nia logu bledow.
                if (response.StatusCode == HttpStatusCode.NotFound)
                    _logger.LogDebug("Allegro GET {Url} returned 404.", url);
                else
                    _logger.LogWarning("Allegro GET {Url} returned {Status}: {Body}",
                        url, (int)response.StatusCode, body.Length <= 300 ? body : body[..300] + "...");

                return default;
            }

            return JsonSerializer.Deserialize<T>(body, _options);
        }

        /// <summary>
        /// Wysyła żądanie, ponawiając je przy błędach przejściowych według wspólnej polityki
        /// (<see cref="HttpRetryPolicy"/>). Wygasły token odświeżamy raz - to nie jest błąd API,
        /// więc nie zużywa puli ponowień.
        /// </summary>
        private async Task<HttpResponseMessage> SendWithRetry(Func<Task<HttpResponseMessage>> send)
        {
            var tokenRefreshed = false;

            for (var attempt = 1; ; attempt++)
            {
                var response = await send();

                if (response.IsSuccessStatusCode)
                    return response;

                // Token mógł wygasnąć w trakcie długiego kroku - odświeżamy go raz i ponawiamy żądanie.
                if (response.StatusCode == HttpStatusCode.Unauthorized && !tokenRefreshed)
                {
                    tokenRefreshed = true;
                    attempt--;

                    var staleToken = response.RequestMessage?.Headers.Authorization?.Parameter;
                    await _auth.RefreshAccessTokenAsync(staleToken);

                    _logger.LogInformation("Allegro returned 401. Token refreshed, retrying the request.");
                    continue;
                }

                if (attempt >= HttpRetryPolicy.MaxAttempts || !HttpRetryPolicy.ShouldRetry(response.StatusCode))
                {
                    // Pozostałe błędy obsługuje wywołujący - on zna kontekst (produkt, ofertę).
                    return response;
                }

                var delay = HttpRetryPolicy.GetDelay(response, attempt);
                var request = response.RequestMessage;

                HttpRetryPolicy.LogRetry(
                    _logger, "Allegro", request?.Method ?? HttpMethod.Get, request?.RequestUri?.PathAndQuery ?? "-",
                    response.StatusCode, attempt, delay);

                await Task.Delay(delay);
            }
        }
    }
}