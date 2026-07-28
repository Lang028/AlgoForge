using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Data
{
    // Development-only recovery for LocalDB, which fails in two ways that both surface as an
    // unhandled SqlException the moment the app starts:
    //
    //   "error: 50 - Local Database Runtime error occurred"
    //   "A network-related or instance-specific error occurred..."
    //
    // The first is simply that LocalDB shut itself down after a few minutes idle and is slow
    // to wake. The second is nastier: the sqlservr.exe process survives while LocalDB's own
    // bookkeeping records the instance as stopped and deregisters its named pipe, so every
    // connection times out AND every "sqllocaldb start" fails with "SQL Server process failed
    // to start" -- because one is already running. Nothing recovers that except stopping the
    // orphan and starting again.
    //
    // Both are handled here rather than left to the developer, because the failure lands as a
    // debugger break in Program.cs and looks like an application bug. Deliberately scoped to
    // Development and to LocalDB connection strings only: nothing here should ever run
    // against a real server.
    public static class DevDatabaseStartup
    {
        // EnableRetryOnFailure does not help here. It retries faults on an established
        // connection; an instance that is not running is not a transient fault, so the very
        // first connection throws before any retry policy is consulted.
        public static async Task<bool> EnsureAvailableAsync(
            AlgoForgeDbContext db, ILogger logger, CancellationToken cancellationToken = default)
        {
            if (await db.Database.CanConnectAsync(cancellationToken))
            {
                return true;
            }

            var instance = LocalDbInstanceName(db.Database.GetConnectionString());
            if (instance is null)
            {
                // Not LocalDB, so there is nothing safe to do automatically.
                logger.LogError(
                    "Cannot reach the database. Check that the server in your connection string is running.");
                return false;
            }

            logger.LogWarning(
                "LocalDB instance {Instance} is not reachable. Attempting to start it...", instance);

            if (await TryStartAsync(instance, logger, cancellationToken))
            {
                return true;
            }

            // Start refused: almost always the orphaned-process case above. Stopping with -k
            // clears the stale registration, after which a normal start works. Only reached
            // once connecting has already failed, so there is no healthy instance to disturb.
            logger.LogWarning(
                "Start failed -- clearing a possible orphaned {Instance} process and retrying.", instance);
            await RunAsync($"stop {instance} -k", logger, cancellationToken);

            if (await TryStartAsync(instance, logger, cancellationToken))
            {
                return true;
            }

            // "stop -k" clears the registration but does not always kill the process: the
            // instance idles out, leaves a live sqlservr behind, and the manager then
            // reports Stopped while that process still holds the instance -- so every
            // subsequent start fails. Observed repeatedly; the only thing that clears it is
            // ending the process itself.
            //
            // Only reached once connecting AND both starts have failed, so there is no
            // working instance to disturb, and the kill is filtered to processes whose
            // command line names this instance so a real SQL Server service is untouched.
            if (await KillOrphanedInstanceProcessesAsync(instance, logger, cancellationToken)
                && await TryStartAsync(instance, logger, cancellationToken))
            {
                return true;
            }

            logger.LogError(
                "Could not start LocalDB instance {Instance} automatically. Run this in a terminal:\n" +
                "    sqllocaldb stop {Instance} -k\n" +
                "    sqllocaldb start {Instance}\n" +
                "If it still refuses, the instance is damaged and can be rebuilt with " +
                "'sqllocaldb delete {Instance}' -- note that drops the local database and the app " +
                "will recreate it empty on the next run.",
                instance, instance, instance, instance);
            return false;
        }

        private static async Task<bool> TryStartAsync(
            string instance, ILogger logger, CancellationToken cancellationToken)
        {
            if (!await RunAsync($"start {instance}", logger, cancellationToken))
            {
                return false;
            }

            // Starting returns before the instance is necessarily accepting connections, so
            // the pipe is polled rather than assumed.
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                // A fresh context per probe: a DbContext caches its failed connection state,
                // and reusing one here reports failure long after the instance is up.
                await using var probe = new AlgoForgeDbContext(
                    new DbContextOptionsBuilder<AlgoForgeDbContext>()
                        .UseSqlServer(ConnectionStringFor(instance))
                        .Options);

                if (await probe.Database.CanConnectAsync(cancellationToken))
                {
                    logger.LogInformation("LocalDB instance {Instance} is up.", instance);
                    return true;
                }
            }

            return false;
        }

        // Set by EnsureAvailableAsync so the probe reconnects with the app's own string.
        private static string? _connectionString;

        public static void UseConnectionString(string connectionString) =>
            _connectionString = connectionString;

        private static string ConnectionStringFor(string instance) =>
            _connectionString ?? $@"Server=(localdb)\{instance};Trusted_Connection=True;";

        // Pulls the instance name out of "Server=(localdb)\mssqllocaldb;...". Returns null for
        // anything that is not LocalDB, which is what keeps this from touching a real server.
        public static string? LocalDbInstanceName(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return null;
            }

            var match = Regex.Match(
                connectionString,
                @"(?:server|data source)\s*=\s*\(localdb\)\\(?<name>[^;]+)",
                RegexOptions.IgnoreCase);

            return match.Success ? match.Groups["name"].Value.Trim() : null;
        }

        // Ends any sqlservr process that is still holding this LocalDB instance.
        //
        // Windows-only and deliberately narrow: the WMI filter matches the instance name in
        // the process command line, which LocalDB passes as its -s argument, so a full SQL
        // Server service (a different instance name, and usually a service account) does not
        // match. Returns true if it killed something worth retrying for.
        private static async Task<bool> KillOrphanedInstanceProcessesAsync(
            string instance, ILogger logger, CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                var script =
                    "$p = Get-CimInstance Win32_Process -Filter \"Name='sqlservr.exe'\" | " +
                    $"Where-Object {{ $_.CommandLine -like '*{instance}*' }}; " +
                    "$p | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }; " +
                    "@($p).Count";

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                var killed = int.TryParse(output.Trim(), out var count) ? count : 0;
                if (killed > 0)
                {
                    logger.LogWarning(
                        "Ended {Count} orphaned sqlservr process(es) still holding {Instance}.",
                        killed, instance);
                }

                return killed > 0;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not check for orphaned {Instance} processes.", instance);
                return false;
            }
        }

        private static async Task<bool> RunAsync(
            string arguments, ILogger logger, CancellationToken cancellationToken)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "sqllocaldb",
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                if (process is null)
                {
                    return false;
                }

                await process.WaitForExitAsync(cancellationToken);
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                // Typically means the SqlLocalDB tooling is not installed or not on PATH.
                logger.LogWarning(ex, "Could not run 'sqllocaldb {Arguments}'.", arguments);
                return false;
            }
        }
    }
}
