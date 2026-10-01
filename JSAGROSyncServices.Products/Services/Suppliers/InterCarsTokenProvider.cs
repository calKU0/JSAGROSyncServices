using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    /// <summary>
    /// Token OAuth2 (client_credentials) do API Inter Cars. Token żyje godzinę, a cykl
    /// synchronizacji trwa dłużej, więc trzymamy go w jednym miejscu i odnawiamy przed wygaśnięciem.
    /// </summary>
    public sealed class InterCarsTokenProvider
    {
        /// <summary>Nazwa klienta HTTP używanego wyłącznie do pobrania tokenu (inny host niż API).</summary>
        public const string AuthHttpClientName = "InterCarsAuth";

        /// <summary>Zapas przed wygaśnięciem - żeby token nie wygasł w trakcie trwającego zapytania.</summary>
        private static readonly TimeSpan ExpirationMargin = TimeSpan.FromMinutes(2);

        private readonly SemaphoreSlim _lock = new(1, 1);
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly InterCarsApiCredentials _credentials;
        private readonly ILogger<InterCarsTokenProvider> _logger;

        private string? _token;
        private DateTimeOffset _expiresAt = DateTimeOffset.MinValue;

        public InterCarsTokenProvider(
            IHttpClientFactory httpClientFactory,
            IOptions<InterCarsApiCredentials> credentials,
            ILogger<InterCarsTokenProvider> logger)
        {
            _httpClientFactory = httpClientFactory;
            _credentials = credentials.Value;
            _logger = logger;
        }

        public async Task<string> GetTokenAsync(CancellationToken ct)
        {
            if (IsValid())
                return _token!;

            await _lock.WaitAsync(ct);

            try
            {
                // Drugi wątek mógł odnowić token, zanim doczekaliśmy się blokady.
                if (IsValid())
                    return _token!;

                return await FetchTokenAsync(ct);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Unieważnia token - wołane po odpowiedzi 401, żeby kolejne zapytanie poszło z nowym.</summary>
        public void Invalidate() => _expiresAt = DateTimeOffset.MinValue;

        private bool IsValid() => _token != null && DateTimeOffset.UtcNow < _expiresAt;

        private async Task<string> FetchTokenAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(_credentials.ClientId) || string.IsNullOrWhiteSpace(_credentials.ClientSecret))
                throw new InvalidOperationException("Brak ClientId/ClientSecret w sekcji InterCarsApiCredentials.");

            using var request = new HttpRequestMessage(HttpMethod.Post, _credentials.AuthUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = _credentials.Scope
                })
            };

            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_credentials.ClientId}:{_credentials.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

            var client = _httpClientFactory.CreateClient(AuthHttpClientName);

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Inter Cars odrzucił żądanie tokenu ({(int)response.StatusCode}): {body}");

            var token = JsonSerializer.Deserialize<TokenResponse>(body);

            if (token == null || string.IsNullOrWhiteSpace(token.AccessToken))
                throw new HttpRequestException("Inter Cars zwrócił pustą odpowiedź tokenu.");

            _token = token.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn, 60)) - ExpirationMargin;

            _logger.LogDebug("Inter Cars token obtained, valid until {ExpiresAt:u}.", _expiresAt);

            return _token;
        }

        private sealed class TokenResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }
    }
}
