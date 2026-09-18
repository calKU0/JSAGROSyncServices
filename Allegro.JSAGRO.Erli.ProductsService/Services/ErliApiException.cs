using System.Net;
using System.Text.Json.Serialization;

namespace Allegro.JSAGRO.Erli.ProductsService.Services
{
    /// <summary>
    /// Błąd zwrócony przez API Erli. Treść jest odczytana z odpowiedzi, a nie wyciągana
    /// regexem z komunikatu wyjątku, więc da się na niej polegać w logach.
    /// </summary>
    public class ErliApiException : Exception
    {
        public ErliApiException(HttpMethod method, string endpoint, HttpStatusCode statusCode, string body, ErliApiError? error)
            : base(BuildMessage(method, endpoint, statusCode, body, error))
        {
            Method = method;
            Endpoint = endpoint;
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }

        public HttpMethod Method { get; }

        public string Endpoint { get; }

        public HttpStatusCode StatusCode { get; }

        /// <summary>Surowa odpowiedź - przydatna, gdy Erli zwróci coś spoza swojego formatu błędów.</summary>
        public string Body { get; }

        public ErliApiError? Error { get; }

        private static string BuildMessage(HttpMethod method, string endpoint, HttpStatusCode statusCode, string body, ErliApiError? error)
        {
            var detail = error?.Error ?? (body.Length <= 300 ? body : body[..300] + "...");

            return $"Erli API {method} {endpoint} failed: {(int)statusCode} {statusCode}. {detail}";
        }
    }

    public class ErliApiError
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("details")]
        public List<ErliApiErrorDetail>? Details { get; set; }
    }

    public class ErliApiErrorDetail
    {
        [JsonPropertyName("field")]
        public string? Field { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
