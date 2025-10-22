using AllegroGaskaProductsSyncService.Data;
using AllegroGaskaProductsSyncService.Repositories;
using AllegroGaskaProductsSyncService.Repositories.Interfaces;
using AllegroGaskaProductsSyncService.Services.Allegro;
using AllegroGaskaProductsSyncService.Services.Allegro.Interfaces;
using AllegroGaskaProductsSyncService.Services.Gaska.Interfaces;
using AllegroGaskaProductsSyncService.Services.GaskaApiService;
using AllegroGaskaProductsSyncService.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "AllegroGaskaProductsSyncService";
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
        services.Configure<GaskaApiCredentials>(configuration.GetSection("GaskaApiCredentials"));
        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

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

        services.AddHttpClient<IGaskaApiService, GaskaApiService>((sp, client) =>
        {
            var gaskaApi = sp.GetRequiredService<IOptions<GaskaApiCredentials>>().Value;
            client.BaseAddress = new Uri(gaskaApi.BaseUrl);
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{gaskaApi.Acronym}|{gaskaApi.Person}:{gaskaApi.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            client.DefaultRequestHeaders.Add("X-Signature", GetSignature(gaskaApi));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // Repositories
        services.AddScoped<ITokenRepository, DbTokenRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IOfferRepository, OfferRepository>();
        services.AddScoped<IImageRepository, ImageRepository>();

        // Services
        services.AddScoped<IAllegroOfferService, AllegroOfferService>();
        services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
        services.AddScoped<IAllegroParametersService, AllegroParametersService>();
        services.AddScoped<IAllegroImageService, AllegroImageService>();

        // Background worker
        services.AddHostedService<Worker>();

        // Dapper
        services.AddSingleton<DapperContext>();

        // Host options
        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
    })
    .UseSerilog()
    .Build();

host.Run();

// Helper to generate signature
static string GetSignature(GaskaApiCredentials apiSettings)
{
    string body = $"acronym={apiSettings.Acronym}&person={apiSettings.Person}&password={apiSettings.Password}&key={apiSettings.Key}";
    using var sha = SHA256.Create();
    byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
    var builder = new StringBuilder();
    foreach (var b in bytes) builder.Append(b.ToString("x2"));
    return builder.ToString();
}