using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
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

        private readonly TimeSpan _interval;
        private readonly int _logsExpirationDays;

        private Timer _timer;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;
        private DateTime _lastRunTime;

        public JSAGROAllegroService()
        {
            // Settings
            var gaskaApiSettings = AppSettingsLoader.LoadApiSettings();
            var allegroApiSettings = AppSettingsLoader.LoadFtpSettings();

            var allegroHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.BaseUrl) };
            var gaskaHttp = new HttpClient { BaseAddress = new Uri(gaskaApiSettings.BaseUrl) };
            var allegroAuthHttp = new HttpClient { BaseAddress = new Uri(allegroApiSettings.AuthBaseUrl) };
            ApiHelper.AddDefaultHeaders(gaskaApiSettings, gaskaHttp);

            _interval = AppSettingsLoader.GetFetchInterval();
            _logsExpirationDays = AppSettingsLoader.GetLogsExpirationDays();

            var dbContext = new MyDbContext();
            var tokenRepo = new DbTokenRepository(dbContext);
            var productRepo = new ProductRepository(dbContext);

            // Services initialization
            var allegroAuthService = new AllegroAuthService(allegroApiSettings, tokenRepo, allegroAuthHttp);
            _allegroApiService = new AllegroApiService(allegroAuthService, productRepo, allegroHttp);
            _gaskaApiService = new GaskaApiService(productRepo, gaskaHttp, gaskaApiSettings);

            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            // Serilog configuration and initialization
            LogConfig.Configure(_logsExpirationDays);

            _timer = new Timer(
                async _ => await TimerTickAsync(),
                null,
                TimeSpan.Zero,
                _interval
            );

            Log.Information("Service started. First run immediately. Interval: {Interval}", _interval);
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

                    // 3. Update Allegro Categories
                    //Log.Information("Starting Allegro categories mapping...");
                    //await _allegroApiService.UpdateAllegroCategories();
                    //Log.Information("Allegro Categories mapping completed.");

                    // 4. Update Allegro Parameters
                    //Log.Information("Starting Allegro parameters for category mapping...");
                    //await _allegroApiService.FetchAndSaveCategoryParameters();
                    //Log.Information("Allegro parameters for category mapping completed.");

                    // 5. Feed Product Parameters
                    //Log.Information("Starting product parameters update...");
                    //await _allegroApiService.UpdateProductParameters();
                    //Log.Information("Product parameters update completed.");

                    // 6. Images Upload
                    //Log.Information("Starting importing images to allegro...");
                    //await _allegroApiService.ImportImages();
                    //Log.Information("Images import completed.");

                    _lastProductDetailsSyncDate = DateTime.Today;
                }

                // 7. Send Allegro offers
                Log.Information("Starting offers upload/update...");
                await _allegroApiService.FetchAndSaveCompatibleProducts();
                Log.Information("Offer upload/update completed");

                DateTime nextRun = _lastRunTime.Add(_interval);
                Log.Information("All processes completed. Next run scheduled at: {NextRun}", nextRun);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during API synchronization.");
            }
        }
    }
}