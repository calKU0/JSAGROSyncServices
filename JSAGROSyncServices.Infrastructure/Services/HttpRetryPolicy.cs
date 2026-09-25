using Microsoft.Extensions.Logging;
using System.Net;

namespace JSAGROSyncServices.Infrastructure.Services
{
    /// <summary>
    /// Wspólna polityka ponawiania zapytań do API partnerów (Allegro, Erli, Inter Cars).
    /// Każde z nich odrzuca nadmiar zapytań kodem 429 i miewa chwilowe błędy bramy,
    /// a różne progi i odstępy w każdym kliencie powodowały tylko, że ten sam problem
    /// zachowywał się inaczej w zależności od serwisu.
    /// </summary>
    public static class HttpRetryPolicy
    {
        /// <summary>Ile razy łącznie wysyłamy zapytanie, zanim oddamy błąd wywołującemu.</summary>
        public const int MaxAttempts = 5;

        /// <summary>Najdłuższe oczekiwanie między próbami - również gdy API poprosi o więcej.</summary>
        private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

        /// <summary>Błędy przejściowe: limit zapytań, przekroczony czas i awarie po stronie API.</summary>
        public static bool ShouldRetry(HttpStatusCode status) =>
            status is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || (int)status >= 500;

        /// <summary>
        /// Odstęp przed kolejną próbą. Nagłówek <c>Retry-After</c> ma pierwszeństwo - API wie lepiej,
        /// kiedy okno limitu się odnowi. Bez niego czekamy 2, 4, 8, 16 sekund.
        /// </summary>
        public static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
        {
            var retryAfter = response.Headers.RetryAfter;

            if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
                return Min(delta, MaxDelay);

            if (retryAfter?.Date is { } date)
            {
                var wait = date - DateTimeOffset.UtcNow;

                if (wait > TimeSpan.Zero)
                    return Min(wait, MaxDelay);
            }

            return Min(TimeSpan.FromSeconds(Math.Pow(2, Math.Clamp(attempt, 1, 6))), MaxDelay);
        }

        /// <summary>
        /// Jednolity wpis w logu o ponowieniu. Dzięki jednej treści widać w logach każdego serwisu
        /// to samo: z którym API, czym i jak długo czekamy.
        /// </summary>
        public static void LogRetry(ILogger logger, string api, HttpMethod method, string url, HttpStatusCode status, int attempt, TimeSpan delay) =>
            logger.LogWarning(
                "{Api} {Method} {Url} returned {Status}. Retry {Attempt}/{MaxAttempts} in {Delay}.",
                api, method, url, (int)status, attempt, MaxAttempts, delay);

        /// <summary>Odstęp, gdy odpowiedzi nie ma wcale - np. po zerwanym połączeniu.</summary>
        public static TimeSpan GetDelay(int attempt) =>
            Min(TimeSpan.FromSeconds(Math.Pow(2, Math.Clamp(attempt, 1, 6))), MaxDelay);

        private static TimeSpan Min(TimeSpan first, TimeSpan second) => first < second ? first : second;
    }
}
