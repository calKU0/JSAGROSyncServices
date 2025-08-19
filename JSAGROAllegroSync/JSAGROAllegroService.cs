using JSAGROAllegroSync.Data;
using JSAGROAllegroSync.DTOs;
using JSAGROAllegroSync.Helpers;
using JSAGROAllegroSync.Logging;
using JSAGROAllegroSync.Services;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

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
            _interval = AppSettingsLoader.GetFetchInterval();
            _logsExpirationDays = AppSettingsLoader.GetLogsExpirationDays();

            var dbContext = new MyDbContext();

            // Services initialization
            _gaskaApiService = new GaskaApiService(dbContext, gaskaApiSettings);
            var tokenRepo = new DbTokenRepository(dbContext);
            var allegroAuthService = new AllegroAuthService(allegroApiSettings, tokenRepo);
            _allegroApiService = new AllegroApiService(allegroAuthService);

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
                await _gaskaApiService.SyncProducts();
                Log.Information("Basic product sync completed.");

                // 2.Getting detailed info about products that are not in db yet
                if (_lastProductDetailsSyncDate.Date < DateTime.Today)
                {
                    await _gaskaApiService.SyncProductDetails();
                    _lastProductDetailsSyncDate = DateTime.Today;

                    Log.Information("Detailed product sync completed.");
                }

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