using AllegroErliSync.Data;
using AllegroErliSync.Helpers;
using AllegroErliSync.Logging;
using AllegroErliSync.Repositories;
using AllegroErliSync.Services;
using AllegroErliSync.Settings;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AllegroErliSync
{
    public partial class AllegroErliSyncService : ServiceBase
    {
        private Timer _timer;
        private DateTime _lastProductDetailsSyncDate = DateTime.MinValue;
        private DateTime _lastRunTime;
        private readonly AppSettings _appSettings;

        public AllegroErliSyncService()
        {
            _appSettings = AppSettingsLoader.LoadAppSettings();
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            LogConfig.Configure(_appSettings.LogsExpirationDays);
            Log.Information("Service starting...");
            _timer = new Timer(
                async _ => await TimerTickAsync(),
                null,
                TimeSpan.Zero,
                Timeout.InfiniteTimeSpan
            );
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
                var dapperContext = new DapperContext();
                var offerRepository = new OfferRepository(dapperContext);

                var erliClient = new ErliClient();
                var erliService = new ErliService(offerRepository, erliClient);

                Log.Information("Starting Erli sync at {Time}", DateTime.Now);
                var start = DateTime.Now;

                await erliService.SyncOffersWithErli();
                await erliService.CreateProductsInErli();

                var duration = DateTime.Now - start;
                Log.Information("Erli sync completed at {Time} (Duration: {Duration})", DateTime.Now, duration);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during Erli sync");
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