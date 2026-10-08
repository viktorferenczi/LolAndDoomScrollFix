using Limiter.Core;
using Limiter.Core.Lol;
using Limiter.Protocol;
using Limiter.Service;
using Limiter.Service.Lcu;

if (args.Contains("--diagnose", StringComparer.OrdinalIgnoreCase))
{
    // Kézi ellenőrzés az aktuális LoL-kliensen (normál felhasználóként is futtatható).
    return await LcuDiagnostics.RunAsync(args.Contains("--test-cancel", StringComparer.OrdinalIgnoreCase));
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = IpcConstants.ServiceName);

var paths = DataPaths.Resolve();
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<SettingsProvider>();
builder.Services.AddSingleton(_ => new UsageStore(paths.DatabasePath));
builder.Services.AddSingleton(sp => new LimitEngine(
    sp.GetRequiredService<UsageStore>(),
    () => sp.GetRequiredService<SettingsProvider>().Current,
    sp.GetRequiredService<IClock>()));
builder.Services.AddSingleton<LolMatchTracker>();
builder.Services.AddSingleton<ProtectionState>();
builder.Services.AddSingleton<StatusBuilder>();
builder.Services.AddSingleton<RequestHandler>();
builder.Services.AddHostedService<PipeServerService>();
builder.Services.AddHostedService<LcuSupervisor>();
builder.Services.AddHostedService<MaintenanceService>();

var host = builder.Build();
await host.RunAsync();
return 0;
