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
builder.Services.AddHostedService<ProvisioningWorker>();

var host = builder.Build();
host.Run();
