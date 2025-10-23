using AllegroErliProductsSyncService.Data;
using AllegroErliProductsSyncService.Repositories;
using AllegroErliProductsSyncService.Services;
using AllegroErliProductsSyncService.Settings;
using Microsoft.Extensions.Options;

namespace AllegroErliProductsSyncService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IOptions<AppSettings> _appSettings;
        private readonly IServiceProvider _serviceProvider;

        public Worker(ILogger<Worker> logger, IOptions<AppSettings> appSettings, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _appSettings = appSettings;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Erli sync worker starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dapperContext = scope.ServiceProvider.GetRequiredService<DapperContext>();
                    var offerRepository = scope.ServiceProvider.GetRequiredService<OfferRepository>();
                    var erliClient = scope.ServiceProvider.GetRequiredService<ErliClient>();
                    var erliService = scope.ServiceProvider.GetRequiredService<ErliService>();

                    _logger.LogInformation("Starting Erli sync at {time}", DateTime.Now);

                    var totalSw = System.Diagnostics.Stopwatch.StartNew();
                    var stepTimes = new Dictionary<string, TimeSpan>();

                    async Task MeasureStepAsync(string stepName, Func<Task> action)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        await action();
                        sw.Stop();
                        stepTimes[stepName] = sw.Elapsed;
                        _logger.LogInformation($"{stepName} completed in {FormatDuration(sw.Elapsed)}.");
                    }

                    await MeasureStepAsync("Sync offers with Erli", () => erliService.SyncOffersWithErli());
                    await MeasureStepAsync("Create products in Erli", () => erliService.CreateProductsInErli());
                    await MeasureStepAsync("Update products in Erli", () => erliService.UpdateProductsInErli());

                    totalSw.Stop();

                    _logger.LogInformation("=== Erli sync completed at {time} ===", DateTime.Now);
                    _logger.LogInformation("=== Step durations ===");

                    foreach (var kv in stepTimes)
                    {
                        _logger.LogInformation($" - {kv.Key}: {FormatDuration(kv.Value)}");
                    }

                    _logger.LogInformation($"=== Total time: {FormatDuration(totalSw.Elapsed)} ===");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during Erli sync");
                }

                int delayMinutes = _appSettings.Value.FetchIntervalMinutes;
                var nextRun = TimeSpan.FromMinutes(delayMinutes);
                _logger.LogInformation("Next run in {minutes} minutes.", delayMinutes);

                await Task.Delay(nextRun, stoppingToken);
            }

            _logger.LogInformation("Erli sync worker stopped.");

            string FormatDuration(TimeSpan timeSpan)
            {
                return $"{(int)timeSpan.TotalMinutes:D2}m {timeSpan.Seconds:D2}s";
            }
        }
    }
}