using Allegro.JSAGRO.Erli.ProductsService.Services;
using Allegro.JSAGRO.Erli.ProductsService.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace Allegro.JSAGRO.Erli.ProductsService
{
    /// <summary>Jeden cykl synchronizacji z Erli. Przebieg identyczny jak w pozostałych usługach.</summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly AppSettings _appSettings;

        public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _appSettings = appSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(_appSettings.FetchIntervalMinutes, 1));

            _logger.LogInformation("Worker started (Erli). Interval: {Interval} minutes.", interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    try
                    {
                        await RunSyncCycleAsync(scope.ServiceProvider.GetRequiredService<ErliService>(), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Synchronization cycle failed.");
                    }
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

        private async Task RunSyncCycleAsync(ErliService erliService, CancellationToken ct)
        {
            var stepTimes = new List<(string Name, TimeSpan Elapsed)>();
            var total = Stopwatch.StartNew();

            _logger.LogInformation("=== Synchronization cycle started ===");

            async Task Step(string name, Func<Task> action)
            {
                var sw = Stopwatch.StartNew();

                try
                {
                    await action();
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Zatrzymanie uslugi w trakcie kroku - to nie jest blad synchronizacji.
                    _logger.LogInformation("Step '{Step}' stopped: service is shutting down.", name);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step '{Step}' failed.", name);
                }

                sw.Stop();
                stepTimes.Add((name, sw.Elapsed));
                _logger.LogInformation("{Step} finished in {Duration}.", name, Format(sw.Elapsed));
            }

            await Step("Responsible producers", () => erliService.SyncResponsibleProducersWithErli(ct));
            await Step("Responsible persons", () => erliService.SyncResponsiblePersonsWithErli(ct));
            await Step("Delivery price lists", () => erliService.SyncDeliveriesWithErli(ct));
            await Step("Existing products", () => erliService.SyncOffersWithErli(ct));
            await Step("Products creation", () => erliService.CreateProductsInErli(ct));
            await Step("Products update", () => erliService.UpdateProductsInErli(ct));

            total.Stop();

            _logger.LogInformation("=== Synchronization cycle finished in {Duration} ===", Format(total.Elapsed));

            foreach (var step in stepTimes.OrderByDescending(s => s.Elapsed).Take(5))
                _logger.LogInformation(" - {Step}: {Duration}", step.Name, Format(step.Elapsed));
        }

        private static string Format(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}s";
    }
}
