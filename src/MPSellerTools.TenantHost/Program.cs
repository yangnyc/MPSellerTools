using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Hosting;
using MPSellerTools.Infrastructure.Notifications;
using MPSellerTools.Infrastructure.Tenants;
using MPSellerTools.TenantHost.Marketplace;
using MPSellerTools.TenantHost.Marketplace.Channels;
using MPSellerTools.TenantHost.Services;
using Serilog;

// Content root must be pinned to the directory containing this assembly, not
// the process's current working directory: the provisioning worker launches
// this exact same compiled binary as a child process (brief §4/§10) and its
// working directory is not guaranteed to be this project's output folder.
// Without this, ASP.NET Core's default WebRootPath resolution (which looks
// for "wwwroot" under the content root) silently returns null and static
// file / SPA-fallback serving breaks — reproduced and fixed during Increment 3.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// This machine's database address and password, kept out of source control
// (see README.md, "Database connection").
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Each running instance of this compiled binary is bound to exactly one
// tenant via a per-instance config file living outside source control and
// outside wwwroot (brief §4/§10) — written by the provisioning worker for a
// provisioned tenant, or by scripts/Setup-Dev.ps1 for the two demo
// companies. Falls back to appsettings.json's placeholder "dev" tenant when
// no instance file is given, purely so `dotnet run` works out of the box.
var instanceConfigPath = builder.Configuration["MPST_INSTANCE_CONFIG_FILE"]
    ?? Environment.GetEnvironmentVariable("MPST_INSTANCE_CONFIG_FILE");
if (!string.IsNullOrEmpty(instanceConfigPath))
{
    if (!File.Exists(instanceConfigPath))
    {
        throw new InvalidOperationException($"MPST_INSTANCE_CONFIG_FILE was set to '{instanceConfigPath}' but that file does not exist.");
    }
    builder.Configuration.AddJsonFile(instanceConfigPath, optional: false, reloadOnChange: false);
}

var tenantOptions = builder.Configuration.GetSection(TenantOptions.SectionName).Get<TenantOptions>()
    ?? throw new InvalidOperationException("Missing required 'Tenant' configuration section.");
builder.Services.AddSingleton(tenantOptions);

var localDataDirectory = LocalDataPaths.Resolve(
    builder.Configuration["Hosting:LocalDataDirectory"], builder.Environment.ContentRootPath);
var keysDirectory = Path.Combine(localDataDirectory, "tenants", tenantOptions.Slug, "keys");
var logsDirectory = Path.Combine(localDataDirectory, "tenants", tenantOptions.Slug, "logs");
var outboxDirectory = Path.Combine(localDataDirectory, "tenants", tenantOptions.Slug, "outbox");
Directory.CreateDirectory(keysDirectory);
Directory.CreateDirectory(logsDirectory);
Directory.CreateDirectory(outboxDirectory);

builder.Services.AddSingleton<IDevOutbox>(new FileDevOutbox(outboxDirectory));

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("TenantSlug", tenantOptions.Slug)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDirectory, $"{tenantOptions.Slug}-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

// This connection string is fixed for the lifetime of the process (brief §4)
// — there is no code path anywhere in this host that picks a database based
// on a request, header, or token.
builder.Services.AddDbContext<TenantDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TenantDatabase")));

builder.Services
    .AddIdentity<TenantUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<TenantDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Includes the slug so cookies from different TenantHost instances never
    // share a name even though they run the same compiled binary (brief §7).
    options.Cookie.Name = $"MPSellerTools.Tenant.{tenantOptions.Slug}.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddDataProtection()
    .SetApplicationName($"MPSellerTools.Tenant.{tenantOptions.Slug}")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = $"MPSellerTools.Tenant.{tenantOptions.Slug}.Xsrf";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "X-CSRF-TOKEN";
});

// The platform console manages this tenant's users by calling this instance
// with the tenant's own platform access key — it is never given this
// database's connection string (brief §4).
var platformAccessKeyPath = PlatformAccessKey.PathFor(localDataDirectory, tenantOptions.Slug);
builder.Services.AddSingleton(new PlatformAccessKeyHolder(PlatformAccessKey.LoadOrCreate(platformAccessKeyPath)));
builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, PlatformAccessAuthenticationHandler>(PlatformAccess.Scheme, null);

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Roles.TenantAdmin, policy => policy.RequireRole(Roles.TenantAdmin));
    options.AddPolicy(Roles.Employee, policy => policy.RequireRole(Roles.TenantAdmin, Roles.Employee));
    // The only policy that looks at the platform access key, so the key
    // reaches user management and no other endpoint.
    options.AddPolicy(PlatformAccess.UserManagementPolicy, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, PlatformAccess.Scheme)
        .RequireRole(Roles.TenantAdmin, Roles.PlatformOperator));
});

// Role changes and blocking must revoke access immediately rather than
// waiting for the cookie's next scheduled revalidation (brief §5). The
// default AddIdentity() wiring only re-checks each user's security stamp
// every 30 minutes; forcing it to every request means a security-stamp bump
// (done by UsersController on role change / block) invalidates that user's
// existing session on their very next call.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

var devSpaOrigins = builder.Configuration.GetSection("Hosting:DevSpaOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevSpa", policy =>
        policy.WithOrigins(devSpaOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<MPSellerTools.TenantHost.Services.AuditLogger>();
builder.Services.AddScoped<InvitationIssuer>();

// eBay: one outgoing HTTP client, read-only calls on behalf of the connected seller.
builder.Services.AddHttpClient(EbayClient.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddScoped<EbayClient>();
builder.Services.AddScoped<EbaySync>();

// The multichannel catalog: one adapter per channel behind a shared sync
// queue kept in this tenant's own database. Nothing here reaches a
// marketplace until Marketplace:LiveWritesEnabled and the account's own
// switch are both on; until then every operation is a dry run.
builder.Services.Configure<MarketplaceOptions>(builder.Configuration.GetSection(MarketplaceOptions.SectionName));
builder.Services.AddHttpClient(ChannelHttp.AmazonClient, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(ChannelHttp.WalmartClient, client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ChannelTokenCache>();
builder.Services.AddSingleton<ChannelRateLimiter>();
builder.Services.AddScoped<ChannelHttp>();
builder.Services.AddScoped<ChannelSecrets>();
builder.Services.AddScoped<IChannelAdapter, EbayChannelAdapter>();
builder.Services.AddScoped<IChannelAdapter, AmazonChannelAdapter>();
builder.Services.AddScoped<IChannelAdapter, WalmartChannelAdapter>();
builder.Services.AddScoped<IChannelAdapter, WebsiteChannelAdapter>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<ListingService>();
builder.Services.AddScoped<OrderIngestionService>();
builder.Services.AddScoped<SyncEngine>();
builder.Services.AddScoped<CatalogBackfill>();
builder.Services.AddScoped<SyncHealthReader>();
builder.Services.AddScoped<LowStockMonitor>();
builder.Services.AddHostedService<ChannelSyncWorker>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevSpa");

    // Dev convenience only — the provisioning worker applies tenant
    // migrations explicitly when creating a real tenant instance (brief §10).
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { Roles.TenantAdmin, Roles.Employee })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var isApi = context.Request.Path.StartsWithSegments("/api");
    var isSafeMethod = HttpMethods.IsGet(context.Request.Method) ||
        HttpMethods.IsHead(context.Request.Method) ||
        HttpMethods.IsOptions(context.Request.Method) ||
        HttpMethods.IsTrace(context.Request.Method);

    // A request carrying the platform access key has no session cookie for a
    // forged request to ride on, so there is nothing for antiforgery to protect.
    var viaPlatformKey = isApi && !isSafeMethod &&
        (await context.AuthenticateAsync(PlatformAccess.Scheme)).Succeeded;

    if (isApi && !isSafeMethod && !viaPlatformKey)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = "Invalid or missing antiforgery token." });
            return;
        }
    }

    await next();
});

app.MapControllers();

app.MapFallback(context =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    context.Response.ContentType = "text/html";
    return context.Response.SendFileAsync(
        Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Run();

// Lets MPSellerTools.Tests use WebApplicationFactory<Program> against this
// host's real pipeline (real EF Core context, real Identity, real
// antiforgery/cookie config) instead of a hand-rolled test double — a
// top-level-statements Program is internal by default and otherwise
// inaccessible outside this assembly.
public partial class Program;
