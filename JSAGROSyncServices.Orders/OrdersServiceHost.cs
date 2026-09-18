using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Infrastructure.Hosting;
using JSAGROSyncServices.Infrastructure.Repositories;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Orders.Configuration;
using JSAGROSyncServices.Orders.Repositories;
using JSAGROSyncServices.Orders.Services;
using JSAGROSyncServices.Orders.Settings;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace JSAGROSyncServices.Orders
{
    /// <summary>
    /// Wspólny host serwisów zamówień - logowanie, migracje, rejestracje DI i worker.
    /// Każdy serwis podaje tylko swoją tożsamość i reguły realizacji.
    /// </summary>
    public static class OrdersServiceHost
    {
        public static void Run(string[] args, OrderServiceContext serviceContext, OrderPipeline pipeline)
        {
            try
            {
                var host = Host.CreateDefaultBuilder(args)
                    .UseWindowsService(options => options.ServiceName = serviceContext.WindowsServiceName)
                    .ConfigureServices((hostContext, services) =>
                    {
                        var configuration = hostContext.Configuration;

                        ServiceBootstrap.ConfigureLogging(configuration);

                        var connectionString = configuration.GetConnectionString("MyDbContext")
                            ?? throw new InvalidOperationException("Brak connection stringa 'MyDbContext' w konfiguracji.");

                        ServiceBootstrap.RunMigrations(connectionString);

                        services.AddSingleton(serviceContext);
                        services.AddSingleton(pipeline);

                        services.Configure<AllegroApiCredentials>(configuration.GetSection("AllegroApiCredentials"));
                        services.Configure<GaskaApiCredentials>(configuration.GetSection("GaskaApiCredentials"));
                        services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
                        services.Configure<CourierSettings>(configuration.GetSection("CourierSettings"));
                        services.Configure<SmtpSettings>(configuration.GetSection("SmtpSettings"));

                        AddHttpClients(services, configuration);

                        services.AddSingleton(_ => new DapperContext(connectionString));

                        services.AddScoped<ITokenRepository, DbTokenRepository>();
                        services.AddScoped<IOrderRepository, OrderRepository>();

                        services.AddScoped<IEmailService, EmailService>();
                        services.AddScoped<IOrderService, OrderService>();

                        services.AddHostedService<Worker>();
                        services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
                    })
                    .UseSerilog()
                    .Build();

                Log.Information("Starting {ServiceName}...", serviceContext.WindowsServiceName);
                host.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Service terminated unexpectedly.");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void AddHttpClients(IServiceCollection services, IConfiguration configuration)
        {
            var userAgent = configuration["AllegroApiCredentials:UserAgent"] ?? "JSAGROSyncServices";

            services.AddHttpClient<AllegroAuthService>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

                client.BaseAddress = new Uri(credentials.AuthBaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            });

            services.AddHttpClient<AllegroApiClient>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

                client.BaseAddress = new Uri(credentials.BaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
            });

            services.AddHttpClient<GaskaOrdersApiClient>((sp, client) =>
            {
                var gaskaApi = sp.GetRequiredService<IOptions<GaskaApiCredentials>>().Value;

                client.BaseAddress = new Uri(gaskaApi.BaseUrl);
                client.Timeout = TimeSpan.FromMinutes(2);

                var credentials = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{gaskaApi.Acronym}|{gaskaApi.Person}:{gaskaApi.Password}"));

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                client.DefaultRequestHeaders.Add("X-Signature", GetGaskaSignature(gaskaApi));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });
        }

        private static string GetGaskaSignature(GaskaApiCredentials apiSettings)
        {
            var body = $"acronym={apiSettings.Acronym}&person={apiSettings.Person}&password={apiSettings.Password}&key={apiSettings.Key}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
