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
// Dev/demo: email is written to App_Data/outbox and listed on the Outbox page, so invite,
// claim and connection links are clickable during a presentation without an SMTP account.
// Anywhere else, real email goes out over SMTP -- see SmtpEmailSender for what it needs
// configured (Email:Host/Port/Username/Password, the last two as secrets, never committed).
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<AlgoForge.Services.IEmailSender, AlgoForge.Services.OutboxEmailSender>();
}
else
{
    builder.Services.Configure<SmtpEmailOptions>(builder.Configuration.GetSection("Email"));
    builder.Services.AddScoped<AlgoForge.Services.IEmailSender, SmtpEmailSender>();
}

builder.Services.AddHttpClient<PersonPipelineService>(client =>
{
    var baseUrl = builder.Configuration["PersonPipeline:BaseUrl"] ?? "http://127.0.0.1:8000";
    client.BaseAddress = new Uri(baseUrl);
    // Clustering is O(n^2) over an event's detections and runs on CPU, so it is far
    // slower than a single detection call -- a few thousand detections can take minutes.
    client.Timeout = TimeSpan.FromMinutes(10);
});

// Face detection runs off the request path -- see PhotoDetectionWorker for why. The queue
// is a singleton (one process, one in-memory list of pending photo ids); the worker itself
// is registered as a hosted service so it starts with the app and drains the queue for as
// long as the app runs.
builder.Services.AddSingleton<PhotoDetectionQueue>();
builder.Services.AddHostedService<PhotoDetectionWorker>();

// Stops LocalDB idling itself out from under the running app -- see LocalDbKeepAlive.
// Development only: a real database server does not go to sleep.
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddHostedService<LocalDbKeepAlive>();
}

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

        if (await DevDatabaseStartup.EnsureAvailableAsync(db, startupLogger))
        {
            await db.Database.MigrateAsync();
            await DbInitializer.SeedAsync(db, userManager, roleManager);
        }
        else
        {
            // Deliberately NOT fatal any more.
            //
            // This used to `return`, which ends the process -- and Visual Studio reports
            // that as "Unable to connect to web server 'AlgoForge'. The web server is no
            // longer running", which says nothing about a database and sends you looking
            // for a bug in the app. Worse, recovering meant starting the whole thing again.
            //
            // Starting anyway is the better trade: the site comes up, the reason is on
            // screen and in the log, and because the connection retries on failure the app
            // heals by itself the moment LocalDB is back -- no restart, no F5.
            startupLogger.LogError(
                "The database is unreachable, so migrations and seeding were skipped. " +
                "The site will start, but pages that read data will fail until LocalDB is " +
                "back. It should recover on its own; if it does not, run: " +
                "sqllocaldb stop mssqllocaldb -k  then  sqllocaldb start mssqllocaldb");
        }
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
        await DbInitializer.SeedAsync(db, userManager, roleManager);
    }
}
else
{
    // Outside Development there is no LocalDB dance and no demo data: a real host's
    // database doesn't go to sleep, and a demo admin/attendee account has no place in a
    // real deployment. But migrations still have to run somewhere, or a fresh production
    // database has zero tables and the very first request throws -- this is that seam,
    // so a deploy doesn't also require someone to run dotnet-ef by hand against it.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
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

// Uploaded photos live in App_Data/uploads, outside wwwroot, so that dev-time file
// watchers (dotnet watch, Visual Studio hot reload) don't treat every upload as a source
// change and restart or refresh the app mid-upload.
//
// They are deliberately NOT mapped to a static-file route. A provider on /uploads served
// every private event's pictures to anyone holding the URL, with no account and no
// membership check -- events are private by default, so the bytes go through
// PhotosController.File, which checks membership of the photo's event on every request.
var uploadsRoot = Path.Combine(app.Environment.ContentRootPath, "App_Data", "uploads");
Directory.CreateDirectory(uploadsRoot);

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program { }
