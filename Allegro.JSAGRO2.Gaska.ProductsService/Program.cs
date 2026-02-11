using Allegro.JSAGRO2.Gaska.ProductsService.Repositories;
using Allegro.JSAGRO2.Gaska.ProductsService.Services.Allegro;
using Allegro.JSAGRO2.Gaska.ProductsService.Settings;
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
        options.ServiceName = "AllegroJSAGRO2GaskaProductsService";
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
        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
        services.Configure<PriceSettings>(configuration.GetSection("PriceSettings"));
        services.Configure<AllegroSettings>(configuration.GetSection("AllegroSettings"));

        // HttpClients
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

        // Repositories
        services.AddScoped<ITokenRepository, DbTokenRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();
        services.AddScoped<IParameterRepository, ParameterRepository>();

        // Services
        services.AddScoped<IAllegroOfferService, AllegroOfferService>();
        services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
        services.AddScoped<IAllegroParametersService, AllegroParametersService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Dapper
        services.AddSingleton(sp => new DapperContext(connectionString));

        // Host options
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
    })
    .UseSerilog()
    .Build();

host.Run();