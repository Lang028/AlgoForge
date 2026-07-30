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

// ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    // Development only: allows short demo passwords like "2345"
    // while presenting.
    if (builder.Environment.IsDevelopment())
    {
        options.Password.RequiredLength = 4;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    }
})
    .AddEntityFrameworkStores<AlgoForgeDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthorization();
builder.Services.AddControllersWithViews();

builder.Services.AddScoped<AttendeeImportService>();

builder.Services.AddScoped<
    AlgoForge.Services.Authorization.IEventAccessService,
    AlgoForge.Services.Authorization.EventAccessService>();

// REAL EMAIL SENDER
// Emails will now be sent through SMTP instead of being written
// to App_Data/outbox.
builder.Services.AddScoped<
    AlgoForge.Services.IEmailSender,
    AlgoForge.Services.SmtpEmailSender>();

// Person pipeline
builder.Services.AddHttpClient<PersonPipelineService>(client =>
{
    var baseUrl = builder.Configuration["PersonPipeline:BaseUrl"]
        ?? "http://127.0.0.1:8000";

    client.BaseAddress = new Uri(baseUrl);

    // Clustering is CPU intensive and may take several minutes.
    client.Timeout = TimeSpan.FromMinutes(10);
});

// LocalDB keep-alive
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

    // Create/upgrade the local development database.
    if (db.Database.IsRelational())
    {
        var startupLogger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("AlgoForge.Startup");

        DevDatabaseStartup.UseConnectionString(connectionString);

        if (await DevDatabaseStartup.EnsureAvailableAsync(db, startupLogger))
        {
            await db.Database.MigrateAsync();

            await DbInitializer.SeedAsync(
                db,
                userManager,
                roleManager);
        }
        else
        {
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
