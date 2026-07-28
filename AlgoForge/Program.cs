using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.Services.PersonPipeline;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// LocalDB shuts itself down after a few minutes idle and is slow to wake, so the first
// query after a quiet spell can fail outright. Retrying transient faults keeps that from
// taking the whole app down -- it bites hardest around uploads, where the pipeline call
// leaves a long gap between database calls.
builder.Services.AddDbContext<AlgoForgeDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AlgoForgeDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();
builder.Services.AddScoped<AttendeeImportService>();
builder.Services.AddScoped<AlgoForge.Services.Authorization.IEventAccessService,
                           AlgoForge.Services.Authorization.EventAccessService>();
builder.Services.AddScoped<AlgoForge.Services.IEmailSender, AlgoForge.Services.NoOpEmailSender>();

builder.Services.AddHttpClient<PersonPipelineService>(client =>
{
    var baseUrl = builder.Configuration["PersonPipeline:BaseUrl"] ?? "http://127.0.0.1:8000";
    client.BaseAddress = new Uri(baseUrl);
    // Clustering is O(n^2) over an event's detections and runs on CPU, so it is far
    // slower than a single detection call -- a few thousand detections can take minutes.
    client.Timeout = TimeSpan.FromMinutes(10);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    // Create/upgrade the local dev database before seeding, so a fresh clone works
    // without needing the dotnet-ef tool installed.
    //
    // Guarded because the integration tests boot this same Program against an in-memory
    // provider, which has no notion of migrations -- unguarded, MigrateAsync throws before
    // any test gets to run, and the failure looks like a broken test rather than a
    // provider mismatch. EnsureCreated covers the in-memory case.
    if (db.Database.IsRelational())
    {
        // LocalDB stops itself when idle, and occasionally leaves an orphaned process behind
        // that no longer answers. Both land here as an unhandled SqlException before the app
        // has started, which reads as an application bug rather than a stopped database, so
        // the instance is woken (and un-wedged) first. Development-only, LocalDB-only.
        var startupLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("AlgoForge.Startup");
        DevDatabaseStartup.UseConnectionString(connectionString);

        if (!await DevDatabaseStartup.EnsureAvailableAsync(db, startupLogger))
        {
            // The logger has already explained what to do. Stopping here beats starting an
            // app whose every page would fail on its first query.
            startupLogger.LogError("Startup aborted: the database is unreachable.");
            return;
        }

        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }
    await DbInitializer.SeedAsync(db, userManager, roleManager);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Uploaded photos live in App_Data/uploads, outside wwwroot, so that dev-time file
// watchers (dotnet watch, Visual Studio hot reload) don't treat every upload as a source
// change and restart or refresh the app mid-upload. This provider serves them at the
// same /uploads URLs the Photo.BlobUrl column has always used.
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(uploadsRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsRoot),
    RequestPath = "/uploads"
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
