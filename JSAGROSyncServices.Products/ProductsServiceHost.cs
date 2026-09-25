using DbUp;
using JSAGROSyncServices.Contracts.Data.Enums;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Contracts.Settings;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Infrastructure.Logging;
using JSAGROSyncServices.Infrastructure.Repositories;
using JSAGROSyncServices.Infrastructure.Services;
using JSAGROSyncServices.Products.Configuration;
using JSAGROSyncServices.Products.Helpers;
using JSAGROSyncServices.Products.Repositories;
using JSAGROSyncServices.Infrastructure.Hosting;
using JSAGROSyncServices.Products.Services.Allegro;
using JSAGROSyncServices.Products.Services.Suppliers;
using JSAGROSyncServices.Products.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace JSAGROSyncServices.Products
{
    /// <summary>
    /// Wspólny host serwisów produktowych - logowanie, migracje bazy, rejestracje DI i worker.
    /// Każdy serwis podaje tylko swoją tożsamość (konto + dostawca) i listę kroków.
    /// </summary>
    public static class ProductsServiceHost
    {
        public static void Run(string[] args, ServiceContext serviceContext, SyncPipeline pipeline)
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
                    services.Configure<RolmarApiCredentials>(configuration.GetSection("RolmarApiCredentials"));
                    services.Configure<InterCarsApiCredentials>(configuration.GetSection("InterCarsApiCredentials"));
                    services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
                    services.Configure<PriceSettings>(configuration.GetSection("PriceSettings"));
                    services.Configure<AllegroSettings>(configuration.GetSection("AllegroSettings"));
                    services.Configure<SmtpSettings>(configuration.GetSection("SmtpSettings"));

                    AddAllegroHttpClients(services, configuration);
                    AddSupplierHttpClients(services, serviceContext, pipeline);

                    services.AddSingleton(_ => new DapperContext(connectionString));

                    services.AddScoped<ITokenRepository, DbTokenRepository>();
                    services.AddScoped<IProductRepository, ProductRepository>();
                    services.AddScoped<IOfferRepository, OfferRepository>();
                    services.AddScoped<IImageRepository, ImageRepository>();
                    services.AddScoped<IParameterRepository, ParameterRepository>();
                    services.AddScoped<ICategoryRepository, CategoryRepository>();
                    services.AddScoped<IAllegroResponsibleProducerRepository>(sp => new AllegroResponsibleProducerRepository(sp.GetRequiredService<DapperContext>(), serviceContext.Account));
                    services.AddScoped<IAllegroResponsiblePersonRepository>(sp => new AllegroResponsiblePersonRepository(sp.GetRequiredService<DapperContext>(), serviceContext.Account));
                    services.AddScoped<IAllegroDeliveryMethodRepository>(sp => new AllegroDeliveryMethodRepository(sp.GetRequiredService<DapperContext>(), serviceContext.Account));
                    services.AddScoped<ISupplierCategoryRepository>(sp => new SupplierCategoryRepository(
                        sp.GetRequiredService<DapperContext>(),
                        serviceContext.Company));
                    services.AddScoped<ISyncCategoryRepository>(sp => new SyncCategoryRepository(
                        sp.GetRequiredService<DapperContext>(),
                        serviceContext.Account,
                        serviceContext.Company));

                    services.AddScoped<IAllegroOfferService, AllegroOfferService>();
                    services.AddScoped<IAllegroCategoryService, AllegroCategoryService>();
                    services.AddScoped<IAllegroParametersService, AllegroParametersService>();
                    services.AddScoped<IAllegroProductService, AllegroProductService>();
                    services.AddScoped<IAllegroResponsibleProducerService, AllegroResponsibleProducerService>();
                    services.AddScoped<IAllegroResponsiblePersonService, AllegroResponsiblePersonService>();
                    services.AddScoped<IAllegroShippingRateService, AllegroShippingRateService>();
                    services.AddScoped<IEmailService, EmailService>();

                    switch (serviceContext.Company)
                    {
                        case IntegrationCompany.Gaska:
                            services.AddScoped<IOfferFactory, GaskaOfferFactory>();
                            break;

                        case IntegrationCompany.InterCars:
                            services.AddScoped<IOfferFactory, InterCarsOfferFactory>();
                            break;

                        default:
                            services.AddScoped<IOfferFactory, RolmarOfferFactory>();
                            break;
                    }

                    services.AddHostedService<Worker>();
                    services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
                })
                .UseSerilog()
                .Build();

            host.Run();
        }

        private static void AddAllegroHttpClients(IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpClient<AllegroAuthService>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

                client.BaseAddress = new Uri(credentials.AuthBaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-www-form-urlencoded"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(configuration["AllegroApiCredentials:UserAgent"] ?? "JSAGROSyncServices");
            });

            services.AddHttpClient<AllegroApiClient>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<AllegroApiCredentials>>().Value;

                client.BaseAddress = new Uri(credentials.BaseUrl);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.allegro.public.v1+json"));
                client.DefaultRequestHeaders.UserAgent.ParseAdd(configuration["AllegroApiCredentials:UserAgent"] ?? "JSAGROSyncServices");
            });
        }

        private static void AddSupplierHttpClients(IServiceCollection services, ServiceContext serviceContext, SyncPipeline pipeline)
        {
            var needsSupplierApi = pipeline.FetchSupplierProducts
                || pipeline.FetchSupplierStock
                || pipeline.FetchSupplierImages
                || pipeline.FetchSupplierProductDetailsDaily;

            if (!needsSupplierApi)
                return;

            if (serviceContext.Company == IntegrationCompany.InterCars)
            {
                AddInterCarsHttpClients(services);
                return;
            }

            if (serviceContext.Company == IntegrationCompany.Gaska)
            {
                services.AddHttpClient<IGaskaApiService, GaskaApiService>((sp, client) =>
                {
                    var gaskaApi = sp.GetRequiredService<IOptions<GaskaApiCredentials>>().Value;

                    client.BaseAddress = new Uri(gaskaApi.BaseUrl);
                    client.Timeout = TimeSpan.FromMinutes(5);

                    var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{gaskaApi.Acronym}|{gaskaApi.Person}:{gaskaApi.Password}"));
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                    client.DefaultRequestHeaders.Add("X-Signature", GetGaskaSignature(gaskaApi));
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                });
            }
            else
            {
                services.AddHttpClient<IRolmarSyncService, RolmarSyncService>((sp, client) =>
                {
                    var rolmarApi = sp.GetRequiredService<IOptions<RolmarApiCredentials>>().Value;

                    client.BaseAddress = new Uri(rolmarApi.BaseUrl);
                    client.Timeout = TimeSpan.FromMinutes(10);
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                });
            }
        }

        /// <summary>
        /// Inter Cars autoryzuje zapytania tokenem OAuth2 z osobnego hosta, dlatego klient
        /// pobierający token jest oddzielny, a token dokłada do zapytań dedykowany handler.
        /// </summary>
        private static void AddInterCarsHttpClients(IServiceCollection services)
        {
            services.AddHttpClient(InterCarsTokenProvider.AuthHttpClientName, client =>
                client.Timeout = TimeSpan.FromMinutes(1));

            services.AddSingleton<InterCarsTokenProvider>();
            services.AddTransient<InterCarsAuthHandler>();

            services.AddHttpClient<IInterCarsApiService, InterCarsApiService>((sp, client) =>
            {
                var interCarsApi = sp.GetRequiredService<IOptions<InterCarsApiCredentials>>().Value;

                client.BaseAddress = new Uri(interCarsApi.BaseUrl);
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(interCarsApi.Language);
            })
            .AddHttpMessageHandler<InterCarsAuthHandler>();
        }

        private static string GetGaskaSignature(GaskaApiCredentials apiSettings)
        {
            var body = $"acronym={apiSettings.Acronym}&person={apiSettings.Person}&password={apiSettings.Password}&key={apiSettings.Key}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(body));

            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
