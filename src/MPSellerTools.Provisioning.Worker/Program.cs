using Microsoft.EntityFrameworkCore;
using MPSellerTools.Infrastructure.Hosting;
using MPSellerTools.Infrastructure.Platform;
using MPSellerTools.Provisioning.Worker;
using Serilog;

// Same content-root pinning rationale — and the same bug, reproduced again
// here — as the two hosts (see MPSellerTools.TenantHost/Program.cs). The
// generic Host builder defaults its content root to the process's current
// working directory just like WebApplication does, which breaks
// appsettings.json (and therefore the platform DB connection string) as
// soon as this worker is launched from anywhere but its own output folder.
var contentRootPath = AppContext.BaseDirectory;
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = contentRootPath,
});

// This machine's database address and password, kept out of source control
// (see README.md, "Database connection").
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

var localDataDirectory = LocalDataPaths.Resolve(
    builder.Configuration["Hosting:LocalDataDirectory"], contentRootPath);
var logsDirectory = Path.Combine(localDataDirectory, "worker", "logs");
Directory.CreateDirectory(logsDirectory);

builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDirectory, "worker-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PlatformDatabase")));

builder.Services.Configure<ProvisioningOptions>(builder.Configuration.GetSection(ProvisioningOptions.SectionName));
builder.Services.AddSingleton(resolver => resolver.GetRequiredService<Microsoft.Extensions.Options.IOptions<ProvisioningOptions>>().Value);
builder.Services.AddSingleton<TenantProcessSupervisor>();
builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => localDataDirectory);
builder.Services.AddScoped<TenantProvisioningService>();

// `--stop-all` is a one-shot mode used by scripts/Stop-Dev.ps1: rather than
// have PowerShell reach into the platform database itself, it asks this
// same worker binary to gracefully stop every tenant process it can verify
// is genuinely running, then exit — reusing the exact verified-PID logic
// the worker already uses everywhere else, instead of a second
// implementation in PowerShell.
var stopAllMode = args.Contains("--stop-all");
if (!stopAllMode)
{
    builder.Services.AddHostedService<ProvisioningWorker>();
}

var host = builder.Build();

if (stopAllMode)
{
    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    var supervisor = scope.ServiceProvider.GetRequiredService<TenantProcessSupervisor>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<MPSellerTools.Provisioning.Worker.ProvisioningWorker>>();

    var tenants = await db.Tenants.Where(t => t.ProcessId != null).ToListAsync();
    foreach (var tenant in tenants)
    {
        if (supervisor.IsRunning(tenant))
        {
            logger.LogInformation("Stopping verified tenant process for {Slug}", tenant.Slug);
            supervisor.Stop(tenant);
        }
    }

    return;
}

host.Run();
