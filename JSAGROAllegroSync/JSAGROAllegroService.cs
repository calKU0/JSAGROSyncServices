using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.DTOs.Settings;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Logging;
using JSAGROAllegroSync.Services;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace JSAGROAllegroSync
{
    public partial class JSAGROAllegroService : ServiceBase
    {
        private readonly GaskaApiService _gaskaApiService;
        private readonly AllegroApiService _allegroApiService;
        private readonly AppSettings _appSettings;

        private Timer _timer;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;
        private DateTime _lastRunTime;

        public JSAGROAllegroService()
        {
            // Settings
            var gaskaApiSettings = AppSettingsLoader.LoadGaskaCredentials();
            var allegroApiSettings = AppSettingsLoader.LoadAllegroCredentials();
            _appSettings = AppSettingsLoader.LoadAppSettings();

            var allegroHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.BaseUrl) };
            var gaskaHttp = new HttpClient { BaseAddress = new Uri(gaskaApiSettings.BaseUrl) };
            var allegroAuthHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.AuthBaseUrl) };
            ApiHelper.AddDefaultHeaders(gaskaApiSettings, gaskaHttp);

            var dbContext = new MyDbContext();
            var tokenRepo = new DbTokenRepository(dbContext);
            var productRepo = new ProductRepository(dbContext);

            // Services initialization
            var allegroAuthService = new AllegroAuthService(allegroApiSettings, tokenRepo, allegroAuthHttp);
            _allegroApiService = new AllegroApiService(allegroAuthService, productRepo, allegroHttp, _appSettings);
            _gaskaApiService = new GaskaApiService(productRepo, gaskaHttp, gaskaApiSettings, _appSettings.CategoriesId);

            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            // Serilog configuration and initialization
            LogConfig.Configure(_appSettings.LogsExpirationDays);

            _timer = new Timer(
                async _ => await TimerTickAsync(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes)
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

                // 1. Get default info about products
                //Log.Information("Starting syncing basic products info...");
                //await _gaskaApiService.SyncProducts();
                //Log.Information("Basic product sync completed.");

                // 2. Get detailed info about products that are not in db yet
                if (_lastProductDetailsSyncDate.Date < DateTime.Today)
                {
                    //Log.Information("Starting syncing product details...");
                    //await _gaskaApiService.SyncProductDetails();
                    //Log.Information("Detailed product sync completed.");

                    //3.Update Allegro Categories
                    //Log.Information("Starting Allegro categories mapping...");
                    //await _allegroApiService.UpdateAllegroCategories();
                    //Log.Information("Allegro Categories mapping completed.");

                    //4.Update Allegro Parameters
                    //Log.Information("Starting Allegro parameters for category mapping...");
                    //await _allegroApiService.FetchAndSaveCategoryParameters();
                    //Log.Information("Allegro parameters for category mapping completed.");

                    //5.Feed Product Parameters
                    //Log.Information("Starting product parameters update...");
                    //await _allegroApiService.UpdateProductParameters();
                    //Log.Information("Product parameters update completed.");

                    //6.Images Upload
                    //Log.Information("Starting importing images to allegro...");
                    //await _allegroApiService.ImportImages();
                    //Log.Information("Images import completed.");

                    //7.Get Compatible Products
                    //Log.Information("Starting offers upload/update...");
                    //await _allegroApiService.FetchAndSaveCompatibleProducts();
                    //Log.Information("Offer upload/update completed");

                    _lastProductDetailsSyncDate = DateTime.Today;
                }

                // 9. Get Allegro offers
                Log.Information("Starting syncing Allegro offers ");
                await _allegroApiService.GetAllegroOffers();
                Log.Information("Allegro offers sync completed");

                // 8. Send Allegro offers
                Log.Information("Starting offers upload/update...");
                await _allegroApiService.CreateOffers();
                Log.Information("Offer upload/update completed");

                DateTime nextRun = _lastRunTime.Add(TimeSpan.FromMinutes(_appSettings.FetchIntervalMinutes));
                Log.Information("All processes completed. Next run scheduled at: {NextRun}", nextRun);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during API synchronization.");
            }
        }
    }
}