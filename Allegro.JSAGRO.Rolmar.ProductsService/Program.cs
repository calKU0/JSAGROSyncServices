using Allegro.JSAGRO.Rolmar.ProductsService;
using Allegro.JSAGRO.Rolmar.ProductsService.Repositories;
using Allegro.JSAGRO.Rolmar.ProductsService.Services.Allegro;
using Allegro.JSAGRO.Rolmar.ProductsService.Services.Interfaces;
using Allegro.JSAGRO.Rolmar.ProductsService.Services.Rolmar;
using Allegro.JSAGRO.Rolmar.ProductsService.Settings;
using DbUp;
using JSAGROSyncServices.Shared.Data;
using JSAGROSyncServices.Shared.Interfaces;
using JSAGROSyncServices.Shared.Logging;
using JSAGROSyncServices.Shared.Services;
using JSAGROSyncServices.Shared.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "AllegroJSAGRORolmarProductsService";
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

        // Bind configuration
        services.Configure<RolmarApiCredentials>(configuration.GetSection("RolmarApiCredentials"));
        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<PriceSettings>(configuration.GetSection("PriceSettings"));
        services.Configure<AllegroSettings>(configuration.GetSection("AllegroSettings"));

        // HttpClients
        services.AddHttpClient<IRolmarSyncService, RolmarSyncService>((sp, client) =>
        {
            var rolmarSettings = sp.GetRequiredService<IOptions<RolmarApiCredentials>>().Value;

            client.BaseAddress = new Uri(rolmarSettings.BaseUrl); // assuming BaseUrl is in your settings
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        services.AddHttpClient<AllegroAuthService>((sp, client) =>
        {
            var allegroApiSettings = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

            client.BaseAddress = new Uri(allegroApiSettings.AuthBaseUrl);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
        });

        services.AddHttpClient<AllegroApiClient>((sp, client) =>
        {
            var allegroApiSettings = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

            client.BaseAddress = new Uri(allegroApiSettings.BaseUrl);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
        });

        // Repositories
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IParameterRepository, ParameterRepository>();
        services.AddScoped<ITokenRepository, DbTokenRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IImageRespository, ImageRepository>();

        // Services
        services.AddSingleton<ISyncStateService, FileSyncStateService>();
        services.AddScoped<IAllegroProductService, AllegroProductService>();
        services.AddScoped<IAllegroOfferService, AllegroOfferService>();
        services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
        services.AddScoped<IAllegroParametersService, AllegroParametersService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Dapper
        services.AddSingleton(sp => new DapperContext(connectionString));

        // Host options
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(5));
    })
    .UseSerilog()
    .Build();

host.Run();