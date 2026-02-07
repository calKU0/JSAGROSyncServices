using Allegro.JSAGRO2.Rolmar.ProductsService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
