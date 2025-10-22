using AllegroGaskaOrdersSyncService.Services.Interfaces;
using AllegroGaskaOrdersSyncService.Settings;
using Microsoft.Extensions.Options;

namespace AllegroGaskaOrdersSyncService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AppSettings _appSettings;
        private readonly IServiceProvider _serviceProvider;

        public Worker(ILogger<Worker> logger, IOptions<AppSettings> appSettings, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _appSettings = appSettings.Value;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes);

            _logger.LogInformation("Worker started. Interval: {Interval} minutes", _appSettings.FetchIntervalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunSyncCycleAsync(stoppingToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in synchronization loop.");
                }

                try
                {
                    _logger.LogInformation("Waiting {Delay} before next run...", interval);
                    await Task.Delay(interval, stoppingToken);
                }
                catch (TaskCanceledException) { }
            }

            _logger.LogInformation("Worker stopped.");
        }

        private async Task RunSyncCycleAsync(CancellationToken ct)
        {
            _logger.LogInformation("=== Starting full synchronization cycle ===");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();

                await orderService.SyncOrdersFromAllegro();
                await orderService.CreateOrdersInGaska();
                //await orderService.UpdateOrderGaskaInfo();
                await orderService.UpdateOrdersInAllegro();

                _logger.LogInformation("=== Synchronization cycle finished successfully ===");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during synchronization cycle.");
            }
        }
    }
}