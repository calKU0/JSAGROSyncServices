using JSAGROSyncServices.Infrastructure.Services;
using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace JSAGROSyncServices.Tests
{
    /// <summary>
    /// Wspólna polityka ponawiania. Odstęp musi respektować <c>Retry-After</c>, bo Inter Cars
    /// blokuje klucz na pełną minutę i podaje w nagłówku moment odblokowania - bez tego
    /// wykładniczy backoff kończyłby się przed końcem okna limitu.
    /// </summary>
    public class HttpRetryPolicyTests
    {
        private static HttpResponseMessage Response(HttpStatusCode status, RetryConditionHeaderValue? retryAfter = null)
        {
            var response = new HttpResponseMessage(status);

            if (retryAfter != null)
                response.Headers.RetryAfter = retryAfter;

            return response;
        }

        [Theory]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.RequestTimeout)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData(HttpStatusCode.BadGateway)]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.GatewayTimeout)]
        public void Retries_transient_failures(HttpStatusCode status)
        {
            Assert.True(HttpRetryPolicy.ShouldRetry(status));
        }

        [Theory]
        // Błędy po naszej stronie - ponowienie z tymi samymi danymi skończy się tak samo.
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        [InlineData(HttpStatusCode.NotFound)]
        [InlineData(HttpStatusCode.UnprocessableEntity)]
        public void Does_not_retry_client_errors(HttpStatusCode status)
        {
            Assert.False(HttpRetryPolicy.ShouldRetry(status));
        }

        [Theory]
        [InlineData(1, 2)]
        [InlineData(2, 4)]
        [InlineData(3, 8)]
        [InlineData(4, 16)]
        public void Without_a_header_the_delay_doubles(int attempt, int expectedSeconds)
        {
            using var response = Response(HttpStatusCode.TooManyRequests);

            Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), HttpRetryPolicy.GetDelay(response, attempt));
        }

        [Fact]
        public void Delay_is_capped_at_one_minute()
        {
            using var response = Response(HttpStatusCode.TooManyRequests);

            Assert.Equal(TimeSpan.FromMinutes(1), HttpRetryPolicy.GetDelay(response, 10));
            Assert.Equal(TimeSpan.FromMinutes(1), HttpRetryPolicy.GetDelay(10));
        }

        [Fact]
        public void Retry_after_in_seconds_wins_over_the_backoff()
        {
            using var response = Response(
                HttpStatusCode.TooManyRequests,
                new RetryConditionHeaderValue(TimeSpan.FromSeconds(45)));

            Assert.Equal(TimeSpan.FromSeconds(45), HttpRetryPolicy.GetDelay(response, 1));
        }

        [Fact]
        public void Retry_after_as_a_date_is_honoured()
        {
            // Tak odpowiada Inter Cars: "retry-after: Thu, 01 Oct 2026 09:02:00 GMT".
            using var response = Response(
                HttpStatusCode.TooManyRequests,
                new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(30)));

            var delay = HttpRetryPolicy.GetDelay(response, 1);

            Assert.InRange(delay, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(31));
        }

        [Fact]
        public void Retry_after_in_the_past_falls_back_to_the_backoff()
        {
            using var response = Response(
                HttpStatusCode.TooManyRequests,
                new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(-30)));

            Assert.Equal(TimeSpan.FromSeconds(2), HttpRetryPolicy.GetDelay(response, 1));
        }

        [Fact]
        public void Retry_after_longer_than_the_cap_is_trimmed()
        {
            using var response = Response(
                HttpStatusCode.TooManyRequests,
                new RetryConditionHeaderValue(TimeSpan.FromHours(1)));

            Assert.Equal(TimeSpan.FromMinutes(1), HttpRetryPolicy.GetDelay(response, 1));
        }
    }
}
