using Allegro.JSAGRO.Gaska.OrdersService;
using Allegro.JSAGRO.Gaska.OrdersService.Repositories;
using Allegro.JSAGRO.Gaska.OrdersService.Services;
using Allegro.JSAGRO.Gaska.OrdersService.Settings;
using DbUp;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Infrastructure.Logging;
using JSAGROSyncServices.Infrastructure.Services;
using Serilog;
using System.Net.Http.Headers;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "AllegroJSAGROGaskaOrdersService";
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

        // HttpClient configuration for external APIs
        services.AddHttpClient<AllegroAuthService>(client =>
        {
            client.BaseAddress = new Uri(configuration["AllegroApiCredentials:AuthBaseUrl"]);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
        });

        services.AddHttpClient<AllegroApiClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["AllegroApiCredentials:BaseUrl"]);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
        });

        services.AddHttpClient<GaskaApiClient>(client =>
        {
            client.BaseAddress = new Uri(configuration["GaskaApiCredentials:BaseUrl"]);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Register Dapper context
        services.AddSingleton(sp => new DapperContext(connectionString));

        // Register repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITokenRepository, DbTokenRepository>();

        // Register services
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
    Log.Information("Starting Allegro.JSAGRO.Gaska.OrdersService as Windows Service...");
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