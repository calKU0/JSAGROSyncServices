using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs;
using GaskaAllegroSync.DTOs.Settings;
using GaskaAllegroSync.Helpers;
using GaskaAllegroSync.Logging;
using GaskaAllegroSync.Repositories;
using GaskaAllegroSync.Services;
using GaskaAllegroSync.Services.AllegroApi;
using GaskaAllegroSync.Services.GaskaApiService;
using Serilog;
using System;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace GaskaAllegroSync
{
    public partial class GaskaAllegroSyncService : ServiceBase
    {
        private readonly HttpClient _allegroHttp;
        private readonly HttpClient _gaskaHttp;
        private readonly HttpClient _allegroAuthHttp;
        private readonly AllegroApiCredentials _allegroApiSettings;
        private readonly GaskaApiCredentials _gaskaApiSettings;
        private readonly AppSettings _appSettings;

        private CancellationTokenSource _cts;
        private Task _workerTask;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;

        public GaskaAllegroSyncService()
        {
            _gaskaApiSettings = AppSettingsLoader.LoadGaskaCredentials();
            _allegroApiSettings = AppSettingsLoader.LoadAllegroCredentials();
            _appSettings = AppSettingsLoader.LoadAppSettings();

            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = 10
            };

            _allegroHttp = new HttpClient(handler, false) { BaseAddress = new Uri(_allegroApiSettings.BaseUrl) };
            _gaskaHttp = new HttpClient(handler, false) { BaseAddress = new Uri(_gaskaApiSettings.BaseUrl) };
            _allegroAuthHttp = new HttpClient(handler, false) { BaseAddress = new Uri(_allegroApiSettings.AuthBaseUrl) };

            ApiHelper.AddDefaultHeaders(_gaskaApiSettings, _gaskaHttp);

            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            LogConfig.Configure(_appSettings.LogsExpirationDays);
            _cts = new CancellationTokenSource();

            _workerTask = Task.Run(() => RunLoopAsync(_cts.Token));

            Log.Information("Service started. Interval: {Interval} minutes", _appSettings.FetchIntervalMinutes);
        }

        protected override void OnStop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }

            try
            {
                if (_workerTask != null)
                    _workerTask.Wait(TimeSpan.FromSeconds(30));
            }
            catch (AggregateException ex)
            {
                Log.Warning(ex, "Worker task stopped with exception.");
            }

            Log.Information("Service stopped.");
            Log.CloseAndFlush();
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            var interval = TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunSyncCycleAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // normal stop
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error in synchronization loop.");
                }

                try
                {
                    Log.Information("Waiting {Delay} before next run...", interval);
                    await Task.Delay(interval, ct);
                }
                catch (TaskCanceledException)
                {
                    // service stopping
                }
            }
        }

        private async Task RunSyncCycleAsync(CancellationToken ct)
        {
            var dbContext = new MyDbContext();
            dbContext.Database.CommandTimeout = 880;
            //dbContext.Configuration.AutoDetectChangesEnabled = false;

            try
            {
                var productRepo = new ProductRepository(dbContext);
                var categoryRepo = new CategoryRepository(dbContext);
                var imageRepo = new ImageRepository(dbContext);
                var offerRepo = new OfferRepository(dbContext);
                var tokenRepo = new DbTokenRepository(dbContext);

                var allegroAuthService = new AllegroAuthService(_allegroApiSettings, tokenRepo, _allegroAuthHttp);
                var allegroApiClient = new AllegroApiClient(allegroAuthService, _allegroHttp);

                var gaskaApiService = new GaskaApiService(productRepo, _gaskaHttp, _gaskaApiSettings, _appSettings.CategoriesId);
                var categoryService = new AllegroCategoryService(productRepo, categoryRepo, allegroApiClient);
                var parametersService = new AllegroParametersService(productRepo, categoryRepo);
                var imageService = new AllegroImageService(imageRepo, allegroApiClient);
                var compatibilityService = new AllegroCompatibilityService(productRepo, allegroApiClient);
                var offerService = new AllegroOfferService(productRepo, offerRepo, categoryRepo, allegroApiClient);

                Log.Information("=== Starting full synchronization cycle ===");

                await gaskaApiService.SyncProducts();
                Log.Information("Basic product sync completed.");

                await offerService.SyncAllegroOffers();
                Log.Information("Allegro offers sync completed.");

                await offerService.SyncAllegroOffersDetails();
                Log.Information("Allegro offers details completed.");

                if (_lastProductDetailsSyncDate.Date < DateTime.Today && DateTime.Now.Hour >= 1 && DateTime.Now.Hour <= 9)
                {
                    await gaskaApiService.SyncProductDetails();
                    Log.Information("Detailed product sync completed.");

                    await categoryService.UpdateAllegroCategories();
                    Log.Information("Allegro categories updated.");

                    await compatibilityService.FetchAndSaveCompatibleProducts();
                    Log.Information("Compatibility products fetched.");

                    _lastProductDetailsSyncDate = DateTime.Today;
                }

                await categoryService.FetchAndSaveCategoryParameters();
                Log.Information("Category parameters fetched.");

                await parametersService.UpdateParameters();
                Log.Information("Product parameters updated.");

                await imageService.ImportImages();
                Log.Information("Images import completed.");

                await offerService.CreateOffers();
                Log.Information("Offers creation completed.");

                await offerService.UpdateOffers();
                Log.Information("Offers update completed.");

                Log.Information("=== Synchronization cycle finished successfully ===");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during synchronization cycle.");
            }
            finally
            {
                try
                {
                    dbContext.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Log.Warning(disposeEx, "Failed to dispose DbContext.");
                }

                GC.Collect();
            }
        }
    }
}