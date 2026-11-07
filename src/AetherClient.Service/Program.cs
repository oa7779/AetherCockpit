using AetherClient.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "AETHER Managed Client");
builder.Services.AddHostedService<ManagedClientWorker>();
await builder.Build().RunAsync();
