using AllegroErliProductsSyncService;
using AllegroErliProductsSyncService.Data;
using AllegroErliProductsSyncService.Repositories;
using AllegroErliProductsSyncService.Services;
using AllegroErliProductsSyncService.Settings;
using Microsoft.Extensions.Options;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "AllegroErliProductsSyncService";
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        var logsExpirationDays = Convert.ToInt32(configuration["AppSettings:LogsExpirationDays"]);
        Directory.CreateDirectory(logDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: logsExpirationDays,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .MinimumLevel.Override("System.Net.Http.HttpClient", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .CreateLogger();

        // Bind configuration
        services.Configure<ErliApiCredentials>(configuration.GetSection("ErliApiCredentials"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // Register dependencies
        services.AddSingleton<DapperContext>();
        services.AddScoped<OfferRepository>();
        services.AddScoped<ErliClient>();
        services.AddScoped<ErliService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Host options
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));
    })
    .UseSerilog()
    .Build();

host.Run();