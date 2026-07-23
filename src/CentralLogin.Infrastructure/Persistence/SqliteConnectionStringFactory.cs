using Microsoft.Data.Sqlite;

namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Builds the SQLite connection string from <see cref="DatabaseOptions"/> and
/// ensures the persistent data directory exists.
/// </summary>
internal static class SqliteConnectionStringFactory
{
    public static string Build(DatabaseOptions options)
    {
        var directory = Path.GetFullPath(options.DataDirectory);
        Directory.CreateDirectory(directory);

        var databasePath = Path.Combine(directory, options.FileName);

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            // Enforce referential integrity at the connection level.
            ForeignKeys = true,
            // Connection pooling improves throughput for the web workload.
            Pooling = true,
        }.ToString();
    }
}
