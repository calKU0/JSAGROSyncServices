using Allegro.JSAGRO.Erli.ProductsService;
using Allegro.JSAGRO.Erli.ProductsService.Constants;
using Allegro.JSAGRO.Erli.ProductsService.Repositories;
using Allegro.JSAGRO.Erli.ProductsService.Services;
using Allegro.JSAGRO.Erli.ProductsService.Settings;
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
        .UseWindowsService(options => options.ServiceName = "AllegroJSAGROErliProductsService")
        .ConfigureServices((hostContext, services) =>
        {
            var configuration = hostContext.Configuration;

            ServiceBootstrap.ConfigureLogging(configuration);

            var connectionString = configuration.GetConnectionString("MyDbContext")
                ?? throw new InvalidOperationException("Brak connection stringa 'MyDbContext' w konfiguracji.");

            ServiceBootstrap.RunMigrations(connectionString);

            services.Configure<ErliApiCredentials>(configuration.GetSection("ErliApiCredentials"));
            services.Configure<AppSettings>(configuration.GetSection("AppSettings"));

            services.AddHttpClient<ErliClient>((sp, client) =>
            {
                var credentials = sp.GetRequiredService<IOptions<ErliApiCredentials>>().Value;

                if (string.IsNullOrWhiteSpace(credentials.BaseUrl))
                    throw new InvalidOperationException("Brak 'ErliApiCredentials:BaseUrl' w konfiguracji.");

                if (string.IsNullOrWhiteSpace(credentials.ApiKey))
                    throw new InvalidOperationException("Brak 'ErliApiCredentials:ApiKey' w konfiguracji.");

                client.BaseAddress = new Uri(credentials.BaseUrl);
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credentials.ApiKey);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            });

            services.AddSingleton(_ => new DapperContext(connectionString));

            services.AddScoped<OfferRepository>();
            services.AddScoped<IAllegroResponsibleProducerRepository>(sp => new AllegroResponsibleProducerRepository(sp.GetRequiredService<DapperContext>(), ServiceConstants.Account));
            services.AddScoped<IAllegroResponsiblePersonRepository>(sp => new AllegroResponsiblePersonRepository(sp.GetRequiredService<DapperContext>(), ServiceConstants.Account));
            services.AddScoped<IAllegroDeliveryMethodRepository>(sp => new AllegroDeliveryMethodRepository(sp.GetRequiredService<DapperContext>(), ServiceConstants.Account));
            services.AddScoped<ErliService>();

            services.AddHostedService<Worker>();
            services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(15));
        })
        .UseSerilog()
        .Build();

    Log.Information("Starting AllegroJSAGROErliProductsService...");
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
