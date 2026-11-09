using Allegro.JSAGRO.InPost.ProductsService.DTOs;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Infrastructure.Services;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.InPost.ProductsService.Services
{
    /// <summary>
    /// Token Client Credentials. Access token InPost żyje kilka minut, więc trzymamy go
    /// w pamięci i odświeżamy z zapasem, zamiast prosić o nowy przy każdym zapytaniu.
    /// </summary>
    public class InPostTokenProvider
    {
        public const string HttpClientName = "InPostAuth";

        private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InPostApiCredentials _credentials;
        private readonly SemaphoreSlim _lock = new(1, 1);

        private string? _token;
        private DateTimeOffset _expiresAt;

        public InPostTokenProvider(IHttpClientFactory httpClientFactory, IOptions<InPostApiCredentials> credentials)
        {
            _httpClientFactory = httpClientFactory;
            _credentials = credentials.Value;
        }

        public async Task<string> GetTokenAsync(CancellationToken ct)
        {
            if (_token != null && DateTimeOffset.UtcNow < _expiresAt - RefreshMargin)
                return _token;

            await _lock.WaitAsync(ct);

            try
            {
                if (_token != null && DateTimeOffset.UtcNow < _expiresAt - RefreshMargin)
                    return _token;

                var http = _httpClientFactory.CreateClient(HttpClientName);

                using var response = await http.PostAsync(_credentials.TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _credentials.ClientId,
                    ["client_secret"] = _credentials.ClientSecret,
                    ["scope"] = _credentials.Scope
                }), ct);

                var body = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                    throw new InPostApiException(HttpMethod.Post, _credentials.TokenUrl, response.StatusCode, body, null);

                var token = JsonSerializer.Deserialize<TokenResponse>(body)
                    ?? throw new InPostApiException(HttpMethod.Post, _credentials.TokenUrl, response.StatusCode, body, null);

                _token = token.AccessToken;
                _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60));

                return _token;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Invalidate() => _token = null;
    }

    /// <summary>Klient API InPost Von Halsky: token, ponawianie wg wspólnej polityki i odczyt błędów.</summary>
    public class InPostClient
    {
        private static readonly HttpMethod Patch = new("PATCH");

        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly ILogger<InPostClient> _logger;
        private readonly HttpClient _http;
        private readonly InPostTokenProvider _tokenProvider;
        private readonly InPostApiCredentials _credentials;

        public InPostClient(ILogger<InPostClient> logger, HttpClient http, InPostTokenProvider tokenProvider, IOptions<InPostApiCredentials> credentials)
        {
            _logger = logger;
            _http = http;
            _tokenProvider = tokenProvider;
            _credentials = credentials.Value;
        }

        /// <summary>Ścieżka w kontekście naszej organizacji (sklepu).</summary>
        public string Org(string path) => $"v1/organizations/{_credentials.OrganizationId}/{path.TrimStart('/')}";

        public Task<T> GetAsync<T>(string endpoint, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, endpoint, null, ct);

        public Task<T> PostAsync<T>(string endpoint, object? body, CancellationToken ct) => SendAsync<T>(HttpMethod.Post, endpoint, body, ct);

        public Task<T> PatchAsync<T>(string endpoint, object? body, CancellationToken ct) => SendAsync<T>(Patch, endpoint, body, ct);

        /// <summary>PATCH całej oferty - InPost przyjmuje tu wyłącznie JSON Merge Patch (RFC 7396).</summary>
        public Task<T> MergePatchAsync<T>(string endpoint, object? body, CancellationToken ct) =>
            SendAsync<T>(Patch, endpoint, body, ct, MergePatchContentType);

        private const string JsonContentType = "application/json";
        private const string MergePatchContentType = "application/merge-patch+json";

        private async Task<T> SendAsync<T>(HttpMethod method, string endpoint, object? body, CancellationToken ct, string contentType = JsonContentType)
        {
            var responseBody = await SendAsync(method, endpoint, body, contentType, ct);

            try
            {
                return JsonSerializer.Deserialize<T>(responseBody, JsonOptions)
                    ?? throw new InPostApiException(method, endpoint, HttpStatusCode.OK, responseBody, null);
            }
            catch (JsonException ex)
            {
                throw new InPostApiException(method, endpoint, HttpStatusCode.OK,
                    $"Nie udało się odczytać odpowiedzi: {ex.Message}. Treść: {responseBody}", null);
            }
        }

        private async Task<string> SendAsync(HttpMethod method, string endpoint, object? body, string contentType, CancellationToken ct)
        {
            var refreshedToken = false;

            for (var attempt = 1; ; attempt++)
            {
                using var request = new HttpRequestMessage(method, endpoint);

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _tokenProvider.GetTokenAsync(ct));
                request.Headers.AcceptLanguage.ParseAdd("pl");

                if (body != null)
                {
                    // Typ bez "charset" - InPost dopasowuje Content-Type dokładnie (inaczej 415).
                    request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, JsonOptions)));
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                }

                using var response = await _http.SendAsync(request, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                    return responseBody;

                // Token mógł wygasnąć między sprawdzeniem a wysłaniem - jedna ponowna próba z nowym.
                if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
                {
                    refreshedToken = true;
                    _tokenProvider.Invalidate();
                    continue;
                }

                if (HttpRetryPolicy.ShouldRetry(response.StatusCode))
                {
                    if (attempt >= HttpRetryPolicy.MaxAttempts)
                    {
                        HttpRetryPolicy.LogGaveUp(_logger, "InPost", method, endpoint, response.StatusCode, attempt);
                    }
                    else
                    {
                        var delay = HttpRetryPolicy.GetDelay(response, attempt);
                        HttpRetryPolicy.LogRetry(_logger, "InPost", method, endpoint, response.StatusCode, attempt, delay);
                        await Task.Delay(delay, ct);
                        continue;
                    }
                }

                throw new InPostApiException(method, endpoint, response.StatusCode, responseBody, TryParseError(responseBody));
            }
        }

        private static InPostErrorResponse? TryParseError(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                return JsonSerializer.Deserialize<InPostErrorResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }

    public class InPostApiException : Exception
    {
        public InPostApiException(HttpMethod method, string endpoint, HttpStatusCode statusCode, string body, InPostErrorResponse? error)
            : base(BuildMessage(method, endpoint, statusCode, body, error))
        {
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }

        public HttpStatusCode StatusCode { get; }
        public string Body { get; }
        public InPostErrorResponse? Error { get; }

        /// <summary>Błąd w czytelnej postaci - do logu i do kolumny LastError.</summary>
        public string Describe()
        {
            if (Error == null)
                return $"{(int)StatusCode}: {Body}";

            var details = Error.Details is { Count: > 0 }
                ? " | " + string.Join("; ", Error.Details.Select(d => $"{d.Field}: {d.Detail}"))
                : string.Empty;

            return $"{(int)StatusCode} {Error.ErrorCode}: {Error.ErrorMessage}{details}";
        }

        private static string BuildMessage(HttpMethod method, string endpoint, HttpStatusCode statusCode, string body, InPostErrorResponse? error) =>
            $"InPost {method} {endpoint} -> {(int)statusCode}: {error?.ErrorMessage ?? body}";
    }
}
