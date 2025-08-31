using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs.Settings;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Logging;
using JSAGROAllegroSync.Repositories;
using JSAGROAllegroSync.Services;
using JSAGROAllegroSync.Services.AllegroApi;
using JSAGROAllegroSync.Services.AllegroApi.Interfaces;
using JSAGROAllegroSync.Services.GaskaApi.Interfaces;
using JSAGROAllegroSync.Services.GaskaApiService;
using Serilog;
using System;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace JSAGROAllegroSync
{
    public partial class JSAGROAllegroService : ServiceBase
    {
        private readonly IGaskaApiService _gaskaApiService;
        private readonly IAllegroCategoryService _categoryService;
        private readonly IAllegroParametersService _parametersService;
        private readonly IAllegroImageService _imageService;
        private readonly IAllegroCompatibilityService _compatibilityService;
        private readonly IAllegroOfferService _offerService;
        private readonly AppSettings _appSettings;

        private Timer _timer;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;
        private DateTime _lastRunTime;

        public JSAGROAllegroService()
        {
            // Load settings
            var gaskaApiSettings = AppSettingsLoader.LoadGaskaCredentials();
            var allegroApiSettings = AppSettingsLoader.LoadAllegroCredentials();
            _appSettings = AppSettingsLoader.LoadAppSettings();

            // HttpClients
            var allegroHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.BaseUrl) };
            var gaskaHttp = new HttpClient { BaseAddress = new Uri(gaskaApiSettings.BaseUrl) };
            var allegroAuthHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.AuthBaseUrl) };
            ApiHelper.AddDefaultHeaders(gaskaApiSettings, gaskaHttp);

            // Database
            var dbContext = new MyDbContext();
            dbContext.Database.CommandTimeout = 180; // 3 minutes
            var tokenRepo = new DbTokenRepository(dbContext);
            var productRepo = new ProductRepository(dbContext);
            var categoryRepo = new CategoryRepository(dbContext);
            var imageRepo = new ImageRepository(dbContext);
            var offerRepo = new OfferRepository(dbContext);

            // Allegro auth + api client
            var allegroAuthService = new AllegroAuthService(allegroApiSettings, tokenRepo, allegroAuthHttp);
            var allegroApiClient = new AllegroApiClient(allegroAuthService, allegroHttp);

            // Services initialization
            _gaskaApiService = new GaskaApiService(productRepo, gaskaHttp, gaskaApiSettings, _appSettings.CategoriesId);
            _categoryService = new AllegroCategoryService(productRepo, categoryRepo, allegroApiClient);
            _parametersService = new AllegroParametersService(productRepo, categoryRepo);
            _imageService = new AllegroImageService(imageRepo, allegroApiClient);
            _compatibilityService = new AllegroCompatibilityService(productRepo, allegroApiClient);
            _offerService = new AllegroOfferService(productRepo, offerRepo, categoryRepo, allegroApiClient);

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
            try
            {
                _lastRunTime = DateTime.Now;

                // 1. Sync base products
                Log.Information("Starting syncing basic products info...");
                await _gaskaApiService.SyncProducts();
                Log.Information("Basic product sync completed.");

                // 2. Sync Allegro offers
                Log.Information("Starting syncing Allegro offers...");
                await _offerService.SyncAllegroOffers();
                Log.Information("Allegro offers sync completed.");

                // 3. Update product details once a day
                if (_lastProductDetailsSyncDate.Date < DateTime.Today)
                {
                    // 3.1 Product details
                    Log.Information("Starting syncing product details...");
                    await _gaskaApiService.SyncProductDetails();
                    Log.Information("Detailed product sync completed.");

                    // 3.2 Allegro categories
                    Log.Information("Starting Allegro categories mapping...");
                    await _categoryService.UpdateAllegroCategories();
                    Log.Information("Allegro Categories mapping completed.");

                    // 3.3 Allegro parameters for categories
                    Log.Information("Starting Allegro parameters for category mapping...");
                    await _categoryService.FetchAndSaveCategoryParameters();
                    Log.Information("Allegro parameters for category mapping completed.");

                    // 3.4 Product parameters
                    Log.Information("Starting product parameters update...");
                    await _parametersService.UpdateParameters();
                    Log.Information("Product parameters update completed.");

                    // 3.5 Compatibility products
                    Log.Information("Starting fetching compatible products...");
                    await _compatibilityService.FetchAndSaveCompatibleProducts();
                    Log.Information("Compatible products fetched.");

                    // 3.6 Images
                    Log.Information("Starting importing images to allegro...");
                    await _imageService.ImportImages();
                    Log.Information("Images import completed.");

                    _lastProductDetailsSyncDate = DateTime.Today;
                }

                // 4.Update Allegro offers
                Log.Information("Starting updating Allegro offers...");
                await _offerService.UpdateOffers();
                Log.Information("Allegro offers update completed.");

                // 5. Create Allegro offers
                Log.Information("Starting Allegro offers creation...");
                await _offerService.CreateOffers();
                Log.Information("Allegro Offer creation completed.");
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