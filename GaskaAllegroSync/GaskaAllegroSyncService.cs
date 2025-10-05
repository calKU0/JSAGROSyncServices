using GaskaAllegroSync.Data;
using GaskaAllegroSync.DTOs;
using GaskaAllegroSync.DTOs.Settings;
using GaskaAllegroSync.Helpers;
using GaskaAllegroSync.Logging;
using GaskaAllegroSync.Repositories;
using GaskaAllegroSync.Services;
using GaskaAllegroSync.Services.AllegroApi;
using GaskaAllegroSync.Services.AllegroApi.Interfaces;
using GaskaAllegroSync.Services.GaskaApi.Interfaces;
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

        private Timer _timer;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;
        private DateTime _lastRunTime;

        public GaskaAllegroSyncService()
        {
            // Load settings
            _gaskaApiSettings = AppSettingsLoader.LoadGaskaCredentials();
            _allegroApiSettings = AppSettingsLoader.LoadAllegroCredentials();
            _appSettings = AppSettingsLoader.LoadAppSettings();

            // HttpClients
            _allegroHttp = new HttpClient { BaseAddress = new Uri(_allegroApiSettings.BaseUrl) };
            _gaskaHttp = new HttpClient { BaseAddress = new Uri(_gaskaApiSettings.BaseUrl) };
            _allegroAuthHttp = new HttpClient { BaseAddress = new Uri(_allegroApiSettings.AuthBaseUrl) };
            ApiHelper.AddDefaultHeaders(_gaskaApiSettings, _gaskaHttp);

            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            // Serilog config
            LogConfig.Configure(_appSettings.LogsExpirationDays);

            _timer = new Timer(
                async _ => await TimerTickAsync(),
                null,
                TimeSpan.Zero,
                Timeout.InfiniteTimeSpan
            );

            Log.Information("Service started. First run immediately. Interval: {Interval}", TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes));
        }

        protected override void OnStop()
        {
            Log.Information("Service stopped.");
            Log.CloseAndFlush();
        }

        private async Task TimerTickAsync()
        {
            using (var dbContext = new MyDbContext())
            {
                dbContext.Database.CommandTimeout = 880;

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

                try
                {
                    _lastRunTime = DateTime.Now;

                    // 1. Sync base products
                    Log.Information("Starting syncing basic products info...");
                    await gaskaApiService.SyncProducts();
                    Log.Information("Basic product sync completed.");

                    // 2. Sync Allegro offers
                    Log.Information("Starting syncing Allegro offers...");
                    await offerService.SyncAllegroOffers();
                    Log.Information("Allegro offers sync completed.");

                    Log.Information("Starting syncing Allegro offers details...");
                    await offerService.SyncAllegroOffersDetails();
                    Log.Information("Allegro offers details sync completed.");

                    // 3. Update product details once a day
                    if (_lastProductDetailsSyncDate.Date < DateTime.Today && DateTime.Now.Hour >= 1 && DateTime.Now.Hour <= 10)
                    {
                        // 3.1 Product details
                        Log.Information("Starting syncing product details...");
                        await gaskaApiService.SyncProductDetails();
                        Log.Information("Detailed product sync completed.");

                        // 3.2 Allegro categories
                        Log.Information("Starting Allegro categories mapping...");
                        await categoryService.UpdateAllegroCategories();
                        Log.Information("Allegro Categories mapping completed.");

                        // 3.5 Compatibility products
                        Log.Information("Starting fetching compatible products...");
                        await compatibilityService.FetchAndSaveCompatibleProducts();
                        Log.Information("Compatible products fetched.");

                        _lastProductDetailsSyncDate = DateTime.Today;
                    }

                    // 3.3 Allegro parameters for categories
                    Log.Information("Starting Allegro parameters for category mapping...");
                    await categoryService.FetchAndSaveCategoryParameters();
                    Log.Information("Allegro parameters for category mapping completed.");

                    // 3.4 Product parameters
                    Log.Information("Starting product parameters update...");
                    await parametersService.UpdateParameters();
                    Log.Information("Product parameters update completed.");

                    // 3.6 Images
                    Log.Information("Starting importing images to allegro...");
                    await imageService.ImportImages();
                    Log.Information("Images import completed.");

                    // 3.7. Create Allegro offers
                    Log.Information("Starting Allegro offers creation...");
                    await offerService.CreateOffers();
                    Log.Information("Allegro Offer creation completed.");

                    // 4.Update Allegro offers
                    Log.Information("Starting updating Allegro offers...");
                    await offerService.UpdateOffers();
                    Log.Information("Allegro offers update completed.");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during API synchronization.");
                }
                finally
                {
                    DateTime nextRun = DateTime.Now.AddHours(2);
                    _timer.Change(TimeSpan.FromHours(2), Timeout.InfiniteTimeSpan);

                    Log.Information("All processes completed. Next run scheduled at: {NextRun}", nextRun);
                }
            }
        }
    }
}