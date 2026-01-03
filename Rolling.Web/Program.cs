using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Rolling.Application;
using Rolling.Infrastructure;
using Rolling.Infrastructure.Services;
using Rolling.Web.Diagnostics;
using Rolling.Web.Middleware;
using Rolling.Web.Realtime;
using Rolling.Web.HostedServices;
using Rolling.Web.Models.Sms;
using Rolling.Web.Services.Sms;
using Rolling.Web.Services.Webhooks;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

ConfigureHostUrls(builder);
ConfigureKestrelLimits(builder);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllersWithViews();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    // Allow controllers (especially Payme JSON-RPC) to handle invalid models manually,
    // instead of ASP.NET Core automatically returning HTTP 400.
    options.SuppressModelStateInvalidFilter = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<WebSocketConnectionManager>();
builder.Services.AddSingleton<ChatRealtimeCoordinator>();
builder.Services.AddScoped<ChatSocketHandler>();
builder.Services.AddScoped<PosterUpdatesSocketHandler>();
builder.Services.AddHostedService<NotificationCleanupService>();
builder.Services.AddSingleton<RouteUsageStore>();
builder.Services.Configure<SmsOptions>(builder.Configuration.GetSection(SmsOptions.SectionName));
builder.Services.AddSingleton<IWebhookMessageStore, InMemoryWebhookMessageStore>();
builder.Services.AddHttpClient<EskizSmsClient>((sp, client) =>
{
    var optionsMonitor = sp.GetRequiredService<IOptionsMonitor<SmsOptions>>();
    var baseUrl = optionsMonitor.CurrentValue.Eskiz.BaseUrl;
    if (string.IsNullOrWhiteSpace(baseUrl))
    {
        baseUrl = "https://notify.eskiz.uz/api";
    }

    if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
    {
        baseUrl += "/";
    }

    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<ISmsService, SmsVerificationService>();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// Ensure required infrastructure (e.g. Redis) is available at startup.
app.Services.EnsureInfrastructure();

// Run database migrations automatically
await RunDatabaseMigrationsAsync(app);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only redirect to HTTPS if an HTTPS port is configured; avoid redirect loops to an unused port.
var httpsPort = app.Configuration.GetValue<int?>("Host:HttpsPort");
if (httpsPort is > 0)
{
    app.UseHttpsRedirection();
}
else
{
    app.Logger.LogInformation("HTTPS redirection disabled (no Host:HttpsPort configured).");
}
app.UseStaticFiles();

app.UseSwagger();
app.UseSwaggerUI();

app.UseRouting();
app.UseMiddleware<RouteUsageMiddleware>();
app.UseAuthorization();
app.UseWebSockets();

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Map("/ws/chat", builder =>
{
    builder.Run(async context =>
    {
        var handler = context.RequestServices.GetRequiredService<ChatSocketHandler>();
        await handler.HandleAsync(context);
    });
});

app.Map("/ws/poster-updates", builder =>
{
    builder.Run(async context =>
    {
        var handler = context.RequestServices.GetRequiredService<PosterUpdatesSocketHandler>();
        await handler.HandleAsync(context);
    });
});

app.Run();

static async Task RunDatabaseMigrationsAsync(WebApplication app)
{
    try
    {
        var configuration = app.Configuration;
        var connectionString = configuration["Postgres:ConnectionString"]
            ?? throw new InvalidOperationException("Postgres connection string not found in configuration");

        var migrationsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "migrations");
        if (!Directory.Exists(migrationsPath))
        {
            migrationsPath = Path.Combine(Directory.GetCurrentDirectory(), "migrations");
        }

        if (!Directory.Exists(migrationsPath))
        {
            app.Logger.LogWarning("Migrations directory not found at {Path}, skipping migrations", migrationsPath);
            return;
        }

        var logger = app.Services.GetRequiredService<ILogger<DatabaseMigrationService>>();
        var migrationService = new DatabaseMigrationService(connectionString, logger, migrationsPath);

        await migrationService.MigrateAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Failed to run database migrations");
        throw;
    }
}

static void ConfigureHostUrls(WebApplicationBuilder builder)
{
    var hostSection = builder.Configuration.GetSection("Host");
    if (!hostSection.Exists())
    {
        return;
    }

    var bindAddress = hostSection.GetValue("BindAddress", "0.0.0.0").Trim();
    var urls = new List<string>();

    var httpPort = hostSection.GetValue<int?>("HttpPort");
    if (httpPort is > 0)
    {
        urls.Add($"http://{bindAddress}:{httpPort}");
    }

    var httpsPort = hostSection.GetValue<int?>("HttpsPort");
    if (httpsPort is > 0)
    {
        urls.Add($"https://{bindAddress}:{httpsPort}");
    }

    var additionalUrls = hostSection.GetSection("AdditionalUrls").Get<string[]>();
    if (additionalUrls is { Length: > 0 })
    {
        foreach (var url in additionalUrls)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                urls.Add(url.Trim());
            }
        }
    }

    if (urls.Count > 0)
    {
        builder.WebHost.UseUrls(urls.ToArray());
    }
}

static void ConfigureKestrelLimits(WebApplicationBuilder builder)
{
    // Read max file size from configuration (default 100 MB)
    var maxFileSizeMB = builder.Configuration.GetValue("FileUpload:MaxFileSizeMB", 100);
    var maxRequestBodySize = maxFileSizeMB * 1024 * 1024; // Convert MB to bytes

    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Limits.MaxRequestBodySize = maxRequestBodySize;
    });

    // Also configure FormOptions for multipart requests
    builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    {
        options.MultipartBodyLengthLimit = maxRequestBodySize;
        options.ValueLengthLimit = int.MaxValue;
    });
}
