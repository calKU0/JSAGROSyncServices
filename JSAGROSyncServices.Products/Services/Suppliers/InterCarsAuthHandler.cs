using System.Net;
using System.Net.Http.Headers;

namespace JSAGROSyncServices.Products.Services.Suppliers
{
    /// <summary>
    /// Dokłada nagłówek Bearer do każdego zapytania do Inter Cars. Po 401 unieważnia token
    /// i powtarza zapytanie raz - dzięki temu odrzucony token nie przewraca całego kroku synchronizacji.
    /// </summary>
    public sealed class InterCarsAuthHandler : DelegatingHandler
    {
        private readonly InterCarsTokenProvider _tokenProvider;

        public InterCarsAuthHandler(InterCarsTokenProvider tokenProvider)
        {
            _tokenProvider = tokenProvider;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Authorize(request, cancellationToken);

            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
                return response;

            response.Dispose();
            _tokenProvider.Invalidate();

            // Wysłanej wiadomości nie wolno wysłać ponownie - powtarzamy jej kopię.
            using var retry = await CloneAsync(request, cancellationToken);
            await Authorize(retry, cancellationToken);

            return await base.SendAsync(retry, cancellationToken);
        }

        private async Task Authorize(HttpRequestMessage request, CancellationToken ct) =>
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await _tokenProvider.GetTokenAsync(ct));

        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version
            };

            foreach (var header in request.Headers)
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (request.Content != null)
            {
                var body = await request.Content.ReadAsByteArrayAsync(ct);
                clone.Content = new ByteArrayContent(body);

                foreach (var header in request.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }
}
