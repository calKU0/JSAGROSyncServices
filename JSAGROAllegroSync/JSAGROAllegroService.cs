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

                // 1. Getting default info about products
                //await _gaskaApiService.SyncProducts();
                //Log.Information("Basic product sync completed.");

                ////// 2.Getting detailed info about products that are not in db yet
                //if (_lastProductDetailsSyncDate.Date < DateTime.Today)
                //{
                //    await _gaskaApiService.SyncProductDetails();
                //    _lastProductDetailsSyncDate = DateTime.Today;

                //    Log.Information("Detailed product sync completed.");
                //}

                await _allegroApiService.UploadProducts();

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