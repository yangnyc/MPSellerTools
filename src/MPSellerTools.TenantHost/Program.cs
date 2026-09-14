using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MPSellerTools.Core.Notifications;
using MPSellerTools.Core.Tenancy;
using MPSellerTools.Infrastructure.Notifications;
using MPSellerTools.Infrastructure.Tenants;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var tenantOptions = builder.Configuration.GetSection(TenantOptions.SectionName).Get<TenantOptions>()
    ?? throw new InvalidOperationException("Missing required 'Tenant' configuration section.");
builder.Services.AddSingleton(tenantOptions);

var localDataDirectory = Path.GetFullPath(
    Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Hosting:LocalDataDirectory"] ?? "../../.local"));
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Roles.TenantAdmin, policy => policy.RequireRole(Roles.TenantAdmin));
    options.AddPolicy(Roles.Employee, policy => policy.RequireRole(Roles.TenantAdmin, Roles.Employee));
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
