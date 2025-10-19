using GaskaAllegroProductsSync.Data;
using GaskaAllegroProductsSync.Repositories;
using GaskaAllegroProductsSync.Repositories.Interfaces;
using GaskaAllegroProductsSync.Services.Allegro;
using GaskaAllegroProductsSync.Services.Allegro.Interfaces;
using GaskaAllegroProductsSync.Services.Gaska.Interfaces;
using GaskaAllegroProductsSync.Services.GaskaApiService;
using GaskaAllegroProductsSync.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

// create builder
var builder = Host.CreateApplicationBuilder(args);

var isDev = builder.Environment.IsDevelopment();

// Logging setup
var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
if (!Directory.Exists(logDirectory))
{
    Directory.CreateDirectory(logDirectory);
}

int expirationDays = builder.Configuration.GetValue<int>("AppSettings:LogsExpirationDays");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.File(
        path: Path.Combine(logDirectory, "log-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: expirationDays,
        shared: true,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

// Configuration bindings
builder.Services.Configure<GaskaApiCredentials>(builder.Configuration.GetSection("GaskaApi"));
builder.Services.Configure<AllegroApiCredentials>(builder.Configuration.GetSection("AllegroApi"));
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// Allegro Auth HttpClient
builder.Services.AddHttpClient<AllegroAuthService>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AllegroApi:AuthBaseUrl"]);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
});

builder.Services.AddHttpClient<AllegroApiClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AllegroApi:BaseUrl"]);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
});

builder.Services.AddHttpClient<IGaskaApiService, GaskaApiService>((sp, client) =>
{
    var gaskaApi = sp.GetRequiredService<IOptions<GaskaApiCredentials>>().Value;
    client.BaseAddress = new Uri(gaskaApi.BaseUrl);
    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{gaskaApi.Acronym}|{gaskaApi.Person}:{gaskaApi.Password}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    client.DefaultRequestHeaders.Add("X-Signature", GetSignature(gaskaApi));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

// Repositories
builder.Services.AddScoped<ITokenRepository, DbTokenRepository>();
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IOfferRepository, OfferRepository>();
builder.Services.AddScoped<IImageRepository, ImageRepository>();

// Services
builder.Services.AddScoped<IAllegroOfferService, AllegroOfferService>();
builder.Services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
builder.Services.AddScoped<IAllegroParametersService, AllegroParametersService>();
builder.Services.AddScoped<IAllegroImageService, AllegroImageService>();

// Register background service
builder.Services.AddHostedService<Worker>();
builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
});

// Dapper
builder.Services.AddSingleton<DapperContext>();

var host = builder.Build();
host.Run();

// Helper to generate signature
static string GetSignature(GaskaApiCredentials apiSettings)
{
    string body = $"acronym={apiSettings.Acronym}&person={apiSettings.Person}&password={apiSettings.Password}&key={apiSettings.Key}";
    using (var sha = SHA256.Create())
    {
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(body));
        var builder = new StringBuilder();
        for (int i = 0; i < bytes.Length; i++)
            builder.Append(bytes[i].ToString("x2"));
        return builder.ToString();
    }
}