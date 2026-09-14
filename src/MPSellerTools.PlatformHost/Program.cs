using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Infrastructure.Hosting;
using MPSellerTools.Infrastructure.Notifications;
using MPSellerTools.Infrastructure.Platform;
using Serilog;

// Content root must be pinned to the directory containing this assembly, not
// the process's current working directory — see the identical comment and
// bug writeup in MPSellerTools.TenantHost/Program.cs (Increment 3).
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var localDataDirectory = LocalDataPaths.Resolve(
    builder.Configuration["Hosting:LocalDataDirectory"], builder.Environment.ContentRootPath);
var keysDirectory = Path.Combine(localDataDirectory, "platform", "keys");
var logsDirectory = Path.Combine(localDataDirectory, "platform", "logs");
var outboxDirectory = Path.Combine(localDataDirectory, "platform", "outbox");
Directory.CreateDirectory(keysDirectory);
Directory.CreateDirectory(logsDirectory);
Directory.CreateDirectory(outboxDirectory);

builder.Services.AddSingleton<IDevOutbox>(new FileDevOutbox(outboxDirectory));

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logsDirectory, "platform-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14));

builder.Services.AddDbContext<PlatformDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("PlatformDatabase")));

builder.Services
    .AddIdentity<PlatformUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<PlatformDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "MPSellerTools.Platform.Auth";
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
    .SetApplicationName("MPSellerTools.Platform")
    .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "MPSellerTools.Platform.Xsrf";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.HeaderName = "X-CSRF-TOKEN";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(MPSellerTools.Core.Tenancy.Roles.PlatformAdmin, policy =>
        policy.RequireRole(MPSellerTools.Core.Tenancy.Roles.PlatformAdmin));
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

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("DevSpa");

    // Dev convenience only — production deployments apply migrations
    // explicitly as part of publishing (docs/windows-deployment.md), never
    // implicitly on every process start.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
    await db.Database.MigrateAsync();

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    if (!await roleManager.RoleExistsAsync(MPSellerTools.Core.Tenancy.Roles.PlatformAdmin))
    {
        await roleManager.CreateAsync(new IdentityRole<Guid>(MPSellerTools.Core.Tenancy.Roles.PlatformAdmin));
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

    if (isApi && !isSafeMethod)
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

// SPA fallback: unknown non-/api routes serve index.html; unknown /api routes stay 404.
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
