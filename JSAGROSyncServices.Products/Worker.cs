using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.Services.Suppliers;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace JSAGROSyncServices.Products
{
    /// <summary>
    /// Jeden cykl synchronizacji dla wszystkich kont i dostawców.
    /// O tym, które kroki się wykonują, decyduje <see cref="SyncPipeline"/> danego serwisu.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ServiceContext _service;
        private readonly SyncPipeline _pipeline;
        private readonly AppSettings _appSettings;

        private DateTime _lastDailyStepDate = DateTime.MinValue;

        public Worker(
            ILogger<Worker> logger,
            IServiceScopeFactory scopeFactory,
            ServiceContext serviceContext,
            SyncPipeline pipeline,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _service = serviceContext;
            _pipeline = pipeline;
            _appSettings = appSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(_appSettings.FetchIntervalMinutes, 1));

            _logger.LogInformation(
                "Worker started ({Account} / {Company}). Interval: {Interval} minutes.",
                _service.Account, _service.Company, interval.TotalMinutes);

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    try
                    {
                        await RunSyncCycleAsync(scope.ServiceProvider, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (AllegroAuthorizationRequiredException)
                    {
                        // Link do autoryzacji jest już w logu - czekamy na użytkownika.
                        _logger.LogError("Synchronization cycle stopped: Allegro account is not authorized.");
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

        private async Task RunSyncCycleAsync(IServiceProvider services, CancellationToken ct)
        {
            var syncCategoryRepo = services.GetRequiredService<ISyncCategoryRepository>();
            var offerService = services.GetRequiredService<IAllegroOfferService>();

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
                catch (Exception ex) when (Unwrap(ex) is AllegroAuthorizationRequiredException authEx)
                {
                    // Bez ważnego tokenu kolejne kroki tylko zaśmieciłyby log błędami - przerywamy cykl.
                    throw authEx;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Step '{Step}' failed.", name);
                }

                sw.Stop();
                stepTimes.Add((name, sw.Elapsed));
                _logger.LogInformation("{Step} finished in {Duration}.", name, Format(sw.Elapsed));
            }

            var configuredCategories = _appSettings.GetConfiguredCategories(_service.Company);

            await Step("Category configuration", () => syncCategoryRepo.ReplaceAccountCategoriesAsync(configuredCategories, ct));

            if (_pipeline.SyncAllegroDictionaries)
            {
                await Step("Allegro dictionaries", async () =>
                {
                    await services.GetRequiredService<IAllegroResponsibleProducerService>().SyncResponsibleProducers();
                    await services.GetRequiredService<IAllegroResponsiblePersonService>().SyncResponsiblePersons();
                    await services.GetRequiredService<IAllegroShippingRateService>().SyncShippingRates();
                });
            }

            if (_pipeline.FetchSupplierProducts)
                await Step("Supplier products", () => FetchSupplierProducts(services));

            if (_pipeline.FetchSupplierStock)
                await Step("Supplier stock", () => FetchSupplierStock(services));

            if (_pipeline.FetchSupplierImages)
                await Step("Supplier images", () => FetchSupplierImages(services));

            // Szczegoly poza oknem nocnym: porcja na cykl, najpierw produkty bez szczegolow,
            // potem najdawniej odswiezane. Bez nich produkt nie ma wagi ani wymiarow,
            // wiec nie przechodzi do wystawienia.
            if (_pipeline.FetchSupplierProductDetails)
                await Step("Supplier product details", () => FetchSupplierProductDetails(services));

            await Step("Allegro offers", () => offerService.SyncAllegroOffers());

            if (_pipeline.SyncOfferDetails)
                await Step("Allegro offer details", () => offerService.SyncAllegroOffersDetails());

            if (IsDailyStepDue())
            {
                if (_pipeline.FetchSupplierCategoryTreeDaily)
                    await Step("Supplier category tree", () => services.GetRequiredService<IInterCarsApiService>().SyncCategoryTreeAsync());

                if (_pipeline.FetchSupplierProductDetailsDaily)
                    await Step("Supplier product details", () => FetchSupplierProductDetails(services));

                if (_pipeline.UpdateAllegroCategoriesDaily)
                    await Step("Allegro categories", () => services.GetRequiredService<IAllegroCategoryService>().UpdateAllegroCategories());

                _lastDailyStepDate = DateTime.Today;
            }

            if (_pipeline.UpdateAllegroCategories)
                await Step("Allegro categories", () => services.GetRequiredService<IAllegroCategoryService>().UpdateAllegroCategories());

            if (_pipeline.SearchAllegroProducts)
                await Step("Allegro products search", () => services.GetRequiredService<IAllegroProductService>().SearchProducts());

            if (_pipeline.SyncProductParameters)
            {
                await Step("Category parameters", () => services.GetRequiredService<IAllegroCategoryService>().FetchAndSaveCategoryParameters());
                await Step("Product parameters", () => services.GetRequiredService<IAllegroParametersService>().UpdateParameters());
            }

            await Step("Offers creation", () => offerService.CreateOffers());
            await Step("Offers update", () => offerService.UpdateOffers());

            total.Stop();

            _logger.LogInformation("=== Synchronization cycle finished in {Duration} ===", Format(total.Elapsed));

            foreach (var step in stepTimes.OrderByDescending(s => s.Elapsed).Take(5))
            {
                _logger.LogInformation(" - {Step}: {Duration}", step.Name, Format(step.Elapsed));
            }
        }

        private Task FetchSupplierProducts(IServiceProvider services) => _service.Company switch
        {
            IntegrationCompany.Gaska => services.GetRequiredService<IGaskaApiService>().SyncProducts(),
            IntegrationCompany.InterCars => services.GetRequiredService<IInterCarsApiService>().SyncProductsAsync(),
            _ => services.GetRequiredService<IRolmarSyncService>().SyncProductsAsync()
        };

        private Task FetchSupplierStock(IServiceProvider services) => _service.Company switch
        {
            IntegrationCompany.InterCars => services.GetRequiredService<IInterCarsApiService>().SyncStockAsync(),
            _ => services.GetRequiredService<IRolmarSyncService>().SyncStockAsync()
        };

        // Inter Cars nie ma wlasnych zdjec - galerie pokazuje produkt z katalogu Allegro,
        // wiec ten krok dotyczy wylacznie dostawcow z wlasnymi plikami.
        private Task FetchSupplierImages(IServiceProvider services) =>
            services.GetRequiredService<IRolmarSyncService>().SyncImagesAsync();

        private Task FetchSupplierProductDetails(IServiceProvider services) => _service.Company switch
        {
            IntegrationCompany.InterCars => services.GetRequiredService<IInterCarsApiService>().SyncProductDetailsAsync(),
            _ => services.GetRequiredService<IGaskaApiService>().SyncProductDetails()
        };

        private bool IsDailyStepDue()
        {
            if (!_pipeline.FetchSupplierProductDetailsDaily
                && !_pipeline.UpdateAllegroCategoriesDaily
                && !_pipeline.FetchSupplierCategoryTreeDaily)
                return false;

            if (_lastDailyStepDate.Date >= DateTime.Today)
                return false;

            var hour = DateTime.Now.Hour;
            return hour >= _pipeline.DailyWindowStartHour && hour <= _pipeline.DailyWindowEndHour;
        }

        private static Exception Unwrap(Exception ex) => ex is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
            ? aggregate.InnerExceptions[0]
            : ex;

        private static string Format(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}s";
    }
}
