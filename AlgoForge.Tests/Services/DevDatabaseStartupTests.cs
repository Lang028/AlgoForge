using AlgoForge.Data;

namespace AlgoForge.Tests.Services
{
    // The recovery in DevDatabaseStartup stops an instance and starts it again, so the check
    // that gates it -- "is this actually LocalDB?" -- is the safety boundary. A false positive
    // would mean issuing sqllocaldb commands on behalf of a connection string pointing at a
    // real server, so the parser is tested rather than trusted.
    public class DevDatabaseStartupTests
    {
        [Theory]
        [InlineData(@"Server=(localdb)\mssqllocaldb;Database=AlgoForgeGeekedOn;Trusted_Connection=True", "mssqllocaldb")]
        [InlineData(@"Data Source=(LocalDB)\MSSQLLocalDB;Initial Catalog=X", "MSSQLLocalDB")]
        [InlineData(@"server=(localdb)\ProjectsV13;Trusted_Connection=True;", "ProjectsV13")]
        public void Recognises_localdb_connection_strings(string connectionString, string expected)
        {
            Assert.Equal(expected, DevDatabaseStartup.LocalDbInstanceName(connectionString));
        }

        // Anything that is not LocalDB must return null, which is what keeps the recovery from
        // ever running against a shared or production server.
        [Theory]
        [InlineData(@"Server=.;Database=X;Trusted_Connection=True")]
        [InlineData(@"Server=tcp:prod.database.windows.net,1433;Initial Catalog=X")]
        [InlineData(@"Server=localhost\SQLEXPRESS;Database=X")]
        [InlineData("")]
        [InlineData(null)]
        public void Ignores_everything_that_is_not_localdb(string? connectionString)
        {
            Assert.Null(DevDatabaseStartup.LocalDbInstanceName(connectionString));
        }
    }
}
