using Allegro.JSAGRO.InPost.ProductsService.Services;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Allegro.JSAGRO.InPost.ProductsService
{
    /// <summary>Cykl synchronizacji ofert z InPost Von Halsky.</summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly AppSettings _appSettings;

        public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime, IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _lifetime = lifetime;
            _appSettings = appSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(_appSettings.FetchIntervalMinutes, 1));

            _logger.LogInformation("Worker started (InPost). Interval: {Interval} minutes. Dry run: {DryRun}.",
                interval.TotalMinutes, _appSettings.DryRun);

            while (!stoppingToken.IsCancellationRequested)
            {
                var total = Stopwatch.StartNew();

                using (var scope = _scopeFactory.CreateScope())
                {
                    try
                    {
                        _logger.LogInformation("=== InPost synchronization cycle started ===");
                        await scope.ServiceProvider.GetRequiredService<InPostService>().SyncAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "InPost synchronization cycle failed.");
                    }
                }

                _logger.LogInformation("=== InPost synchronization cycle finished in {Duration} ===", total.Elapsed.ToString(@"mm\m\ ss\s"));

                if (_appSettings.RunOnce)
                {
                    _lifetime.StopApplication();
                    break;
                }

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                }
            }

            _logger.LogInformation("Worker stopped.");
        }
    }
}
