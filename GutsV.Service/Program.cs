using GutsV.Service;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

IHost host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "GutsV RGB Service";
    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
