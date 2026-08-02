using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.Services.PersonPipeline;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found.");

// LocalDB shuts itself down after a few minutes idle and is slow to wake, so the first
// query after a quiet spell can fail outright. Retrying transient faults keeps that from
// taking the whole app down -- it bites hardest around uploads, where the pipeline call
// leaves a long gap between database calls.
builder.Services.AddDbContext<AlgoForgeDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sql => sql.EnableRetryOnFailure()));

// ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AlgoForgeDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<AttendeeImportService>();

// Photo bytes: local disk in development, Azure Blob in production. The container
// filesystem an App Service gives us is wiped on every restart and redeploy, so anything
// written there outlives its Photo row by exactly as long as the container does.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<AlgoForge.Services.IPhotoStorage,
        AlgoForge.Services.LocalPhotoStorage>();
}
else
{
    builder.Services.AddScoped<AlgoForge.Services.IPhotoStorage,
        AlgoForge.Services.BlobPhotoStorage>();
}

builder.Services.AddScoped<
    AlgoForge.Services.Authorization.IEventAccessService,
    AlgoForge.Services.Authorization.EventAccessService>();

// REAL EMAIL SENDER
// Use SmtpEmailSender in Development and Production.
// The SMTP settings are read from:
// Smtp:Host
// Smtp:Port
// Smtp:Username
// Smtp:Password
// Smtp:From
//
// These values can be supplied through User Secrets during development.
builder.Services.AddScoped<
    AlgoForge.Services.IEmailSender,
    AlgoForge.Services.SmtpEmailSender>();

// Person pipeline
builder.Services.AddHttpClient<PersonPipelineService>(client =>
{
    var baseUrl = builder.Configuration["PersonPipeline:BaseUrl"]
        ?? "http://127.0.0.1:8000";

    client.BaseAddress = new Uri(baseUrl);

    // Clustering is O(n^2) over an event's detections and runs on CPU, so it is far
    // slower than a single detection call -- a few thousand detections can take minutes.
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Face detection runs off the request path -- see PhotoDetectionWorker for why.
// The queue is a singleton (one process, one in-memory list of pending photo ids);
// the worker itself is registered as a hosted service.
builder.Services.AddSingleton<PhotoDetectionQueue>();
builder.Services.AddHostedService<PhotoDetectionWorker>();

// Stops LocalDB idling itself out from under the running app.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<LocalDbKeepAlive>();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AlgoForgeDbContext>();

    var userManager = scope.ServiceProvider
        .GetRequiredService<UserManager<ApplicationUser>>();

    var roleManager = scope.ServiceProvider
        .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

    // Create/upgrade the local development database before seeding.
    if (db.Database.IsRelational())
    {
        var startupLogger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AlgoForge.Startup");

        DevDatabaseStartup.UseConnectionString(connectionString);

        if (await DevDatabaseStartup.EnsureAvailableAsync(
                db,
                startupLogger))
        {
            await db.Database.MigrateAsync();

            await DbInitializer.SeedAsync(
                db,
                userManager,
                roleManager);
        }
        else
        {
            // Deliberately continue starting the site even when LocalDB
            // is temporarily unavailable.
            startupLogger.LogError(
                "The database is unreachable, so migrations and seeding were skipped. " +
                "The site will start, but pages that read data may fail until LocalDB is back. " +
                "If necessary, run: " +
                "sqllocaldb stop mssqllocaldb -k  then  sqllocaldb start mssqllocaldb");
        }
    }
    else
    {
        await db.Database.EnsureCreatedAsync();

        await DbInitializer.SeedAsync(
            db,
            userManager,
            roleManager);
    }
}
else
{
    // Production / non-development startup.
    // Run migrations against a real relational database.
    using var scope = app.Services.CreateScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AlgoForgeDbContext>();

    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Uploaded photos live in App_Data/uploads, outside wwwroot.
var uploadsRoot = Path.Combine(
    app.Environment.ContentRootPath,
    "App_Data",
    "uploads");

Directory.CreateDirectory(uploadsRoot);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }