using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync.Services.AllegroApi
{
    public class AllegroApiClient
    {
        private readonly AllegroAuthService _auth;
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _options;

        public AllegroApiClient(AllegroAuthService auth, HttpClient http)
        {
            _auth = auth;
            _http = http;

            _options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        public async Task<T> GetAsync<T>(string url, CancellationToken ct)
        {
            var request = await CreateRequest(HttpMethod.Get, url, ct);
            var response = await _http.SendAsync(request, ct);
            return await Deserialize<T>(response);
        }

        public async Task<T> PostAsync<T>(string url, object body, CancellationToken ct, string contentType = "application/vnd.allegro.public.v1+json")
        {
            var request = await CreateRequest(HttpMethod.Post, url, ct);
            if (body != null)
            {
                var json = JsonSerializer.Serialize(body, _options);
                request.Content = new StringContent(json, Encoding.UTF8, contentType);
            }

            var response = await _http.SendAsync(request, ct);
            return await Deserialize<T>(response);
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

        private async Task<T> Deserialize<T>(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Log.Error("Allegro API error {Status}: {Body}", response.StatusCode, body);
                return default;
            }

            return JsonSerializer.Deserialize<T>(body, _options);
        }
    }
}