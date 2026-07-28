using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Data
{
    // Keeps the LocalDB instance awake while the app is running.
    //
    // LocalDB's automatic ("RANU") instance shuts itself down after a period with no
    // connections -- its own log says so plainly:
    //
    //     The RANU instance is terminating in response to its internal time out.
    //
    // That timeout is not the `user instance timeout` sp_configure setting, which applies
    // to SQL Express user instances; setting that to its maximum changes nothing here. The
    // timer is driven by connections, so the reliable way to stop it firing is to keep
    // using the connection.
    //
    // Why it matters: the instance dying mid-session is what made uploads fail, and worse,
    // it often leaves an orphaned sqlservr process that blocks the next start entirely --
    // so the following run of the app could not reach a database at all.
    //
    // A single trivial query every few minutes is enough. Development-only; a real server
    // does not go to sleep.
    public class LocalDbKeepAlive : BackgroundService
    {
        // Comfortably inside LocalDB's idle window, which is measured in tens of minutes.
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(4);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LocalDbKeepAlive> _logger;

        public LocalDbKeepAlive(IServiceScopeFactory scopeFactory, ILogger<LocalDbKeepAlive> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    // A fresh scope each time: holding one DbContext for the life of the
                    // app would pin a connection and hide exactly the failure this is
                    // meant to prevent.
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();

                    await db.Database.ExecuteSqlRawAsync("SELECT 1", stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Never throw from here. A background service that faults takes the
                    // host down with it, which is the very thing being avoided -- the app
                    // should outlive a sleeping database.
                    _logger.LogWarning(ex,
                        "Keep-alive ping failed; LocalDB may be restarting. The app will keep running.");
                }
            }
        }
    }
}
