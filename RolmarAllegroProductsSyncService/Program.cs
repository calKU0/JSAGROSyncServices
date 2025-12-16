using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Services;
using JSAGROSyncServices.Shared.Settings;
using Microsoft.Extensions.Options;
using RolmarAllegroProductsSyncService;
using RolmarAllegroProductsSyncService.Data;
using RolmarAllegroProductsSyncService.Repositories;
using RolmarAllegroProductsSyncService.Repositories.Interfaces;
using RolmarAllegroProductsSyncService.Services.Allegro;
using RolmarAllegroProductsSyncService.Services.Interfaces;
using RolmarAllegroProductsSyncService.Services.Rolmar;
using RolmarAllegroProductsSyncService.Settings;
using Serilog;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "RolmarAllegroProductsSyncService";
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
        services.Configure<RolmarApiCredentials>(configuration.GetSection("RolmarApiCredentials"));
        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

        // HttpClients
        services.AddHttpClient<IRolmarSyncService, RolmarSyncService>((sp, client) =>
        {
            var rolmarSettings = sp.GetRequiredService<IOptions<RolmarApiCredentials>>().Value;

            client.BaseAddress = new Uri(rolmarSettings.BaseUrl); // assuming BaseUrl is in your settings
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {rolmarSettings.ApiKey}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // Repositories
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IParameterRepository, ParameterRepository>();
        services.AddScoped<ITokenRepository, DbTokenRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();

        // Services
        services.AddScoped<AllegroAuthService>();
        services.AddScoped<AllegroApiClient>();
        services.AddScoped<IAllegroOfferService, AllegroOfferService>();
        services.AddScoped<IRolmarSyncService, RolmarSyncService>();
        services.AddScoped<RolmarAllegroProductsSyncService.Services.Interfaces.IAllegroImageService, AllegroImageService>();
        services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
        services.AddScoped<IAllegroParametersService, AllegroParametersService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Dapper
        services.AddSingleton<DapperContext>();

        // Host options
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
    })
    .UseSerilog()
    .Build();

host.Run();