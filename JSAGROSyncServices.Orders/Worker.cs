using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Orders.Configuration;
using JSAGROSyncServices.Orders.Settings;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace JSAGROSyncServices.Orders
{
    /// <summary>
    /// Jeden cykl obsługi zamówień: Allegro -> baza -> Gąska -> Allegro.
    /// Przebieg jest ten sam dla wszystkich kont.
    /// </summary>
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrderServiceContext _service;
        private readonly AppSettings _appSettings;

        private bool _outsideWorkingHoursLogged;

        public Worker(
            ILogger<Worker> logger,
            IServiceScopeFactory scopeFactory,
            OrderServiceContext serviceContext,
            IOptions<AppSettings> appSettings)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _service = serviceContext;
            _appSettings = appSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(_appSettings.FetchIntervalMinutes, 1));

            _logger.LogInformation(
                "Worker started ({Account} / {Company}). Interval: {Interval} minutes, working hours: {StartHour}-{EndHour}.",
                _service.Account, _service.Company, interval.TotalMinutes, _appSettings.StartHour, _appSettings.EndHour);

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
            if (!IsWithinWorkingHours())
            {
                // Log tylko przy wejściu w przerwę - inaczej zaśmieciłby plik co kilka minut.
                if (!_outsideWorkingHoursLogged)
                {
                    _logger.LogInformation("Outside working hours ({StartHour}-{EndHour}). Orders are not processed.",
                        _appSettings.StartHour, _appSettings.EndHour);

                    _outsideWorkingHoursLogged = true;
                }

                return;
            }

            _outsideWorkingHoursLogged = false;

            var orderService = services.GetRequiredService<IOrderService>();
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

            await Step("Orders from Allegro", () => orderService.SyncOrdersFromAllegro(ct));
            await Step("Orders to Gąska", () => orderService.CreateOrdersInGaska(ct));
            await Step("Gąska order info", () => orderService.UpdateOrderGaskaInfo(ct));
            await Step("Orders to Allegro", () => orderService.UpdateOrdersInAllegro(ct));

            total.Stop();

            _logger.LogInformation("=== Synchronization cycle finished in {Duration} ===", Format(total.Elapsed));

            foreach (var step in stepTimes.OrderByDescending(s => s.Elapsed))
                _logger.LogInformation(" - {Step}: {Duration}", step.Name, Format(step.Elapsed));
        }

        private bool IsWithinWorkingHours()
        {
            if (_appSettings.StartHour == _appSettings.EndHour)
                return true;

            var hour = DateTime.Now.Hour;

            return _appSettings.StartHour < _appSettings.EndHour
                ? hour >= _appSettings.StartHour && hour < _appSettings.EndHour
                : hour >= _appSettings.StartHour || hour < _appSettings.EndHour;
        }

        private static Exception Unwrap(Exception ex) => ex is AggregateException aggregate && aggregate.InnerExceptions.Count > 0
            ? aggregate.InnerExceptions[0]
            : ex;

        private static string Format(TimeSpan elapsed) => $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}s";
    }
}
