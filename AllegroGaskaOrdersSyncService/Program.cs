using AllegroGaskaOrdersSyncService;
using AllegroGaskaOrdersSyncService.Data;
using AllegroGaskaOrdersSyncService.Logging;
using AllegroGaskaOrdersSyncService.Repositories;
using AllegroGaskaOrdersSyncService.Repositories.Interfaces;
using AllegroGaskaOrdersSyncService.Services;
using AllegroGaskaOrdersSyncService.Services.Interfaces;
using AllegroGaskaOrdersSyncService.Settings;
using DbUp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Reflection;

var builder = Host.CreateApplicationBuilder(args);

// ------------------ Serilog setup ------------------
var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var logsExpirationDays = builder.Configuration.GetValue<int>("AppSettings:LogsExpirationDays", 14);

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

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

// ------------------ Database migration ------------------
var connectionString = builder.Configuration.GetConnectionString("MyDbContext");
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
    return;
}
Log.Information("Database migration completed successfully.");

// ------------------ Dependency Injection ------------------

// Configure options
builder.Services.Configure<GaskaApiCredentials>(builder.Configuration.GetSection("GaskaApiCredentials"));
builder.Services.Configure<AllegroApiCredentials>(builder.Configuration.GetSection("AllegroApiCredentials"));
builder.Services.Configure<CourierSettings>(builder.Configuration.GetSection("CourierSettings"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Register Dapper context
builder.Services.AddSingleton<DapperContext>();

// Register repositories
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<ITokenRepository, DbTokenRepository>();

// Register HttpClients
builder.Services.AddHttpClient<AllegroApiClient>();
builder.Services.AddHttpClient<GaskaApiClient>();

// Register services
builder.Services.AddScoped<AllegroAuthService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// Add hosted service
builder.Services.AddHostedService<Worker>();

// ------------------ Build and run host ------------------
try
{
    Log.Information("Starting host...");
    var host = builder.Build();
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}