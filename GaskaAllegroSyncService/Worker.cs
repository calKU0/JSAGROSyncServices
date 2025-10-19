using GaskaAllegroSyncService.Services.Allegro;
using GaskaAllegroSyncService.Services.Allegro.Interfaces;
using GaskaAllegroSyncService.Services.Gaska.Interfaces;
using GaskaAllegroSyncService.Settings;
using Microsoft.Extensions.Options;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AppSettings _appSettings;
    private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;

    public Worker(ILogger<Worker> logger, IServiceScopeFactory scopeFactory, IOptions<AppSettings> appSettings)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _appSettings = appSettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes);

        _logger.LogInformation("Worker started. Interval: {Interval} minutes", _appSettings.FetchIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;

            try
            {
                await RunSyncCycleAsync(services, stoppingToken);
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

    private async Task RunSyncCycleAsync(IServiceProvider services, CancellationToken ct)
    {
        var gaskaApiService = services.GetRequiredService<IGaskaApiService>();
        var allegroApiClient = services.GetRequiredService<AllegroApiClient>();
        var allegroAuthService = services.GetRequiredService<AllegroAuthService>();

        var offerService = services.GetRequiredService<IAllegroOfferService>();
        var categoryService = services.GetRequiredService<IAllegroCategoryService>();
        var parametersService = services.GetRequiredService<IAllegroParametersService>();
        var imageService = services.GetRequiredService<IAllegroImageService>();

        _logger.LogInformation("=== Starting full synchronization cycle ===");

        try
        {
            await gaskaApiService.SyncProducts();
            _logger.LogInformation("Basic product sync completed.");

            await offerService.SyncAllegroOffers();
            _logger.LogInformation("Allegro offers sync completed.");

            await offerService.SyncAllegroOffersDetails();
            _logger.LogInformation("Allegro offers details completed.");

            if (_lastProductDetailsSyncDate.Date < DateTime.Today && DateTime.Now.Hour >= 1 && DateTime.Now.Hour <= 8)
            {
                await gaskaApiService.SyncProductDetails();
                _logger.LogInformation("Detailed product sync completed.");

                await categoryService.UpdateAllegroCategories();
                _logger.LogInformation("Allegro categories updated.");

                _lastProductDetailsSyncDate = DateTime.Today;
            }

            await categoryService.FetchAndSaveCategoryParameters();
            _logger.LogInformation("Category parameters fetched.");

            await parametersService.UpdateParameters();
            _logger.LogInformation("Product parameters updated.");

            await imageService.ImportImages();
            _logger.LogInformation("Images import completed.");

            await offerService.CreateOffers();
            _logger.LogInformation("Offers creation completed.");

            await offerService.UpdateOffers();
            _logger.LogInformation("Offers update completed.");

            _logger.LogInformation("=== Synchronization cycle finished successfully ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during synchronization cycle.");
        }
    }
}