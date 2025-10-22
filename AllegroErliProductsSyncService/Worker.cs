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
                    var start = DateTime.Now;

                    await erliService.SyncOffersWithErli();
                    await erliService.CreateProductsInErli();
                    await erliService.UpdateProductsInErli();

                    var duration = DateTime.Now - start;
                    _logger.LogInformation("Erli sync completed at {time} (Duration: {duration})", DateTime.Now, duration);
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
        }
    }
}