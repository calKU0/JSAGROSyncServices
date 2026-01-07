using AllegroGaskaOrdersSyncService;
using AllegroGaskaOrdersSyncService.Data;
using AllegroGaskaOrdersSyncService.Logging;
using AllegroGaskaOrdersSyncService.Repositories;
using AllegroGaskaOrdersSyncService.Repositories.Interfaces;
using AllegroGaskaOrdersSyncService.Services;
using AllegroGaskaOrdersSyncService.Services.Interfaces;
using AllegroGaskaOrdersSyncService.Settings;
using DbUp;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "AllegroGaskaOrdersSyncService";
    })
    .ConfigureServices((hostContext, services) =>
    {
        var configuration = hostContext.Configuration;

        // ------------------ Logging setup ------------------
        var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var logsExpirationDays = configuration.GetValue<int>("AppSettings:LogsExpirationDays", 14);

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

        // ------------------ Database migration ------------------
        var connectionString = configuration.GetConnectionString("MyDbContext");
        EnsureDatabase.For.SqlDatabase(connectionString);

        var upgrader = DeployChanges.To
            .SqlDatabase(connectionString)
            .LogTo(new SerilogUpgradeLog(Log.Logger))
            .WithScriptsFromFileSystem(Path.Combine(AppContext.BaseDirectory, "Migrations"))
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Log.Error(result.Error.ToString());
            throw result.Error;
        }

        Log.Information("Database migration completed successfully.");

        // ------------------ Dependency Injection ------------------

        // Configure options
        services.Configure<GaskaApiCredentials>(configuration.GetSection("GaskaApiCredentials"));
        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
        services.Configure<CourierSettings>(configuration.GetSection("CourierSettings"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<SmtpSettings>(configuration.GetSection("SmtpSettings"));

        // Register Dapper context
        services.AddSingleton<DapperContext>();

        // Register repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITokenRepository, DbTokenRepository>();

        // Register HttpClients
        services.AddHttpClient<AllegroApiClient>();
        services.AddHttpClient<GaskaApiClient>();

        // Register services
        services.AddScoped<AllegroAuthService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IOrderService, OrderService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Graceful shutdown timeout
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
    })
    .UseSerilog()
    .Build();

try
{
    Log.Information("Starting AllegroGaskaOrdersSyncService as Windows Service...");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}