using Allegro.JSAGRO.InPost.ProductsService;
using Allegro.JSAGRO.InPost.ProductsService.Repositories;
using Allegro.JSAGRO.InPost.ProductsService.Services;
using Allegro.JSAGRO.InPost.ProductsService.Settings;
using JSAGROSyncServices.Contracts.Interfaces;
using JSAGROSyncServices.Infrastructure.Data;
using JSAGROSyncServices.Infrastructure.Hosting;
using JSAGROSyncServices.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;

try
{
    var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService(options => options.ServiceName = "AllegroJSAGROInPostProductsService")
        .ConfigureServices((hostContext, services) =>
        {
            var configuration = hostContext.Configuration;

            ServiceBootstrap.ConfigureLogging(configuration);

            var connectionString = configuration.GetConnectionString("MyDbContext")
                ?? throw new InvalidOperationException("Brak connection stringa 'MyDbContext' w konfiguracji.");

            ServiceBootstrap.RunMigrations(connectionString);

            services.Configure<InPostApiCredentials>(configuration.GetSection("InPostApiCredentials"));
            services.Configure<AppSettings>(configuration.GetSection("AppSettings"));
            services.Configure<InPostPriceSettings>(configuration.GetSection("PriceSettings"));
            services.Configure<InPostOfferSettings>(configuration.GetSection("OfferSettings"));

            services.AddHttpClient(InPostTokenProvider.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
            services.AddSingleton<InPostTokenProvider>();

            services.AddHttpClient<InPostClient>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<InPostApiCredentials>>().Value;

                if (string.IsNullOrWhiteSpace(credentials.BaseUrl))
                    throw new InvalidOperationException("Brak 'InPostApiCredentials:BaseUrl' w konfiguracji.");

                if (string.IsNullOrWhiteSpace(credentials.ClientId) || string.IsNullOrWhiteSpace(credentials.ClientSecret))
                    throw new InvalidOperationException("Brak 'InPostApiCredentials:ClientId' lub 'ClientSecret' w konfiguracji.");

                client.BaseAddress = new Uri(credentials.BaseUrl.EndsWith('/') ? credentials.BaseUrl : credentials.BaseUrl + "/");
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddSingleton(_ => new DapperContext(connectionString));

            services.AddScoped<InPostOfferRepository>();
            services.AddScoped<InPostCatalog>();
            services.AddScoped<IAllegroResponsibleProducerRepository>(sp => new AllegroResponsibleProducerRepository(sp.GetRequiredService<DapperContext>(), InPostService.Account));
            services.AddScoped<IAllegroResponsiblePersonRepository>(sp => new AllegroResponsiblePersonRepository(sp.GetRequiredService<DapperContext>(), InPostService.Account));
            services.AddScoped<InPostService>();

            services.AddHostedService<Worker>();
            services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
        })
        .UseSerilog()
        .Build();

    Log.Information("Starting AllegroJSAGROInPostProductsService...");
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
