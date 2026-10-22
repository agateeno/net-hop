using NetHop.Core.Planning;
using NetHop.Engine;
using NetHop.Host;
using NetHop.Ipc;
using NetHop.Service;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options => options.ServiceName = NetHopPipe.ServiceName);

builder.Services.AddSingleton(new DataPaths(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
builder.Services.AddSingleton<CimNetworkBackend>();
builder.Services.AddSingleton<INetworkBackend>(sp => sp.GetRequiredService<CimNetworkBackend>());
builder.Services.AddSingleton<ProfileApplier>();
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<RuleStore>();
builder.Services.AddSingleton<StateStore>();
builder.Services.AddSingleton<HistoryStore>();
builder.Services.AddSingleton<NetHopController>();
builder.Services.AddHostedService<PipeServer>();
builder.Services.AddHostedService<NetworkMonitor>();

var host = builder.Build();
host.Run();
