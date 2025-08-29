namespace JSAGROAllegroSync.Helpers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public static class AsyncHelpers
    {
        public static async Task ForEachAsync<T>(
            IEnumerable<T> source,
            int degreeOfParallelism,
            Func<T, CancellationToken, Task> body,
            CancellationToken cancellationToken = default)
        {
            using (var semaphore = new SemaphoreSlim(degreeOfParallelism))
            {
                var tasks = source.Select(async item =>
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await body(item, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
    }
}