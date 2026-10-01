using JSAGROSyncServices.Contracts.DTOs.Allegro;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace JSAGROSyncServices.Infrastructure.Services
{
    public class AllegroAuthService
    {
        // Jeden serwis obsługuje jedno konto Allegro, a token odświeżamy pojedynczo:
        // refresh token jest rotowany, więc równoległe odświeżanie unieważnia go dla pozostałych wątków.
        private static readonly SemaphoreSlim TokenGate = new SemaphoreSlim(1, 1);

        private static Task? _deviceFlowTask;
        private static string? _deviceFlowUrl;

        private readonly ILogger<AllegroAuthService> _logger;
        private readonly AllegroApiCredentials _settings;
        private readonly ITokenRepository _tokenRepo;
        private readonly HttpClient _http;

        private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public AllegroAuthService(ILogger<AllegroAuthService> logger, IOptions<AllegroApiCredentials> settings, ITokenRepository tokenRepo, HttpClient httpClient)
        {
            _logger = logger;
            _settings = settings.Value;
            _tokenRepo = tokenRepo;
            _http = httpClient;
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default(CancellationToken))
        {
            var tokens = await _tokenRepo.GetTokensAsync();

            if (tokens != null && !tokens.IsExpired() && !string.IsNullOrWhiteSpace(tokens.AccessToken))
                return tokens.AccessToken;

            return await AcquireTokenAsync(tokens?.AccessToken, ct);
        }

        /// <summary>
        /// Wymusza odświeżenie tokenu po odpowiedzi 401. Jeśli inny wątek zdążył już odświeżyć token,
        /// zwracany jest ten nowy - dzięki temu 401 na kilkudziesięciu równoległych żądaniach
        /// nie wywołuje kilkudziesięciu odświeżeń.
        /// </summary>
        public Task<string> RefreshAccessTokenAsync(string? staleAccessToken, CancellationToken ct = default)
        {
            return AcquireTokenAsync(staleAccessToken, ct);
        }

        private async Task<string> AcquireTokenAsync(string? staleAccessToken, CancellationToken ct)
        {
            await TokenGate.WaitAsync(ct);

            try
            {
                var tokens = await _tokenRepo.GetTokensAsync();

                // Ktoś mógł odświeżyć token, kiedy czekaliśmy na semafor.
                if (tokens != null && !tokens.IsExpired()
                    && !string.IsNullOrWhiteSpace(tokens.AccessToken)
                    && !string.Equals(tokens.AccessToken, staleAccessToken, StringComparison.Ordinal))
                    return tokens.AccessToken;

                if (tokens != null && !string.IsNullOrWhiteSpace(tokens.RefreshToken))
                {
                    try
                    {
                        var refreshed = await RefreshWithRefreshTokenAsync(tokens.RefreshToken, ct);
                        await _tokenRepo.SaveTokensAsync(refreshed);

                        _logger.LogInformation("Allegro access token refreshed.");

                        if (!string.IsNullOrWhiteSpace(refreshed.AccessToken))
                            return refreshed.AccessToken;
                    }
                    catch (HttpRequestException ex)
                    {
                        _logger.LogWarning(ex, "Refreshing the Allegro token failed.");
                    }
                }

                var url = await EnsureDeviceFlowRunningAsync(ct);
                throw new AllegroAuthorizationRequiredException(url);
            }
            finally
            {
                TokenGate.Release();
            }
        }

        /// <summary>
        /// Uruchamia (jeśli nie trwa) autoryzację device flow w tle i zwraca link do autoryzacji.
        /// Serwis działa bez użytkownika, więc nie blokujemy cyklu synchronizacji na oczekiwaniu -
        /// cykl kończy się błędem z linkiem w logu, a token zapisze się sam, gdy ktoś ten link kliknie.
        /// </summary>
        private async Task<string> EnsureDeviceFlowRunningAsync(CancellationToken ct)
        {
            if (_deviceFlowTask is { IsCompleted: false } && !string.IsNullOrEmpty(_deviceFlowUrl))
                return _deviceFlowUrl;

            var device = await StartDeviceFlowAsync(ct);
            _deviceFlowUrl = device.VerificationUriComplete ?? string.Empty;

            _logger.LogError(
                "Allegro requires user authorization. Open {Url} (code: {Code}). The link is valid for {Minutes} minutes - synchronization stays down until it is authorized.",
                device.VerificationUriComplete,
                FormatUserCode(device.UserCode ?? string.Empty),
                Math.Max(device.ExpiresIn / 60, 1));

            var tokenRepo = _tokenRepo;
            var logger = _logger;

            _deviceFlowTask = Task.Run(async () =>
            {
                try
                {
                    var tokens = await PollForDeviceTokenAsync(device, CancellationToken.None);
                    await tokenRepo.SaveTokensAsync(tokens);

                    logger.LogInformation("Allegro authorization completed, token saved.");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Allegro device flow was not completed.");
                }
            });

            return _deviceFlowUrl;
        }

        private async Task<TokenDto> RefreshWithRefreshTokenAsync(string refreshToken, CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, "/auth/oauth/token"))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasic(_settings.ClientId, _settings.ClientSecret));
                req.Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string,string>("grant_type","refresh_token"),
                    new KeyValuePair<string,string>("refresh_token", refreshToken)
                });

                using (var resp = await _http.SendAsync(req, ct))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var body = await resp.Content.ReadAsStringAsync();
                        throw new HttpRequestException($"Refresh failed: {(int)resp.StatusCode} {resp.ReasonPhrase}. Body: {body}");
                    }

                    var json = await resp.Content.ReadAsStringAsync();
                    var tr = JsonSerializer.Deserialize<TokenResponseDto>(json, _jsonOptions)
                        ?? throw new HttpRequestException("Allegro zwróciło pustą odpowiedź przy odświeżaniu tokenu.");

                    return new TokenDto
                    {
                        AccessToken = tr.AccessToken,
                        RefreshToken = tr.RefreshToken,
                        ExpiryDateUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn)
                    };
                }
            }
        }

        private async Task<DeviceCodeResponseDto> StartDeviceFlowAsync(CancellationToken ct)
        {
            var uri = $"/auth/oauth/device?client_id={Uri.EscapeDataString(_settings.ClientId)}";

            using (var req = new HttpRequestMessage(HttpMethod.Post, uri))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasic(_settings.ClientId, _settings.ClientSecret));
                req.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

                using (var resp = await _http.SendAsync(req, ct))
                {
                    resp.EnsureSuccessStatusCode();

                    var json = await resp.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<DeviceCodeResponseDto>(json, _jsonOptions)
                        ?? throw new HttpRequestException("Allegro zwróciło pustą odpowiedź przy starcie autoryzacji urządzenia.");
                }
            }
        }

        private async Task<TokenDto> PollForDeviceTokenAsync(DeviceCodeResponseDto device, CancellationToken ct)
        {
            var startedUtc = DateTime.UtcNow;
            var expiresAtUtc = startedUtc.AddSeconds(device.ExpiresIn);
            var intervalSec = Math.Max(device.Interval, 5);

            while (DateTime.UtcNow < expiresAtUtc)
            {
                ct.ThrowIfCancellationRequested();

                using (var req = new HttpRequestMessage(HttpMethod.Post, "/auth/oauth/token"))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasic(_settings.ClientId, _settings.ClientSecret));
                    req.Content = new FormUrlEncodedContent(new[]
                    {
                        new KeyValuePair<string,string>("grant_type","urn:ietf:params:oauth:grant-type:device_code"),
                        new KeyValuePair<string,string>("device_code", device.DeviceCode ?? string.Empty)
                    });

                    using (var resp = await _http.SendAsync(req, ct))
                    {
                        var body = await resp.Content.ReadAsStringAsync();

                        if (resp.IsSuccessStatusCode)
                        {
                            var tr = JsonSerializer.Deserialize<TokenResponseDto>(body, _jsonOptions)
                                ?? throw new HttpRequestException("Allegro zwróciło pustą odpowiedź przy pobieraniu tokenu.");

                            return new TokenDto
                            {
                                AccessToken = tr.AccessToken,
                                RefreshToken = tr.RefreshToken,
                                ExpiryDateUtc = DateTime.UtcNow.AddSeconds(tr.ExpiresIn)
                            };
                        }

                        if (resp.StatusCode == HttpStatusCode.BadRequest)
                        {
                            var error = TryGetError(body);

                            switch (error)
                            {
                                case "authorization_pending":
                                    await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
                                    continue;

                                case "slow_down":
                                    intervalSec += 5;
                                    await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
                                    continue;

                                case "access_denied":
                                    throw new InvalidOperationException("User denied access in Allegro device flow.");

                                case "expired_token":
                                case "expired_device_code":
                                    throw new TimeoutException("Device code expired before authorization was completed.");

                                default:
                                    throw new HttpRequestException($"Device flow polling failed: {body}");
                            }
                        }

                        throw new HttpRequestException($"Device flow polling HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}. Body: {body}");
                    }
                }
            }

            throw new TimeoutException("Device flow timed out.");
        }

        private static string BuildBasic(string clientId, string clientSecret)
        {
            var bytes = Encoding.UTF8.GetBytes(clientId + ":" + clientSecret);
            return Convert.ToBase64String(bytes);
        }

        private static string? TryGetError(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.TryGetProperty("error", out var e))
                        return e.GetString();
                }
            }
            catch
            {
                // ignore parse errors
            }
            return null;
        }

        private static string FormatUserCode(string userCode)
        {
            if (string.IsNullOrWhiteSpace(userCode)) return userCode;

            userCode = userCode.Replace(" ", "");
            var sb = new StringBuilder();
            for (int i = 0; i < userCode.Length; i++)
            {
                if (i > 0 && i % 3 == 0) sb.Append(' ');
                sb.Append(userCode[i]);
            }
            return sb.ToString();
        }
    }
}