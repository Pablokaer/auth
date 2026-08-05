namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Configuration for where the application database lives. Bound from the
/// <c>Database</c> configuration section.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Which EF Core provider to use. Defaults to
    /// <see cref="DatabaseProvider.Sqlite"/> (local development); production
    /// deployments set this to <see cref="DatabaseProvider.Postgres"/> so the
    /// application database is shared across replicas instead of living on
    /// container-local disk.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;

    /// <summary>SQLite only. Directory that holds the SQLite file(s). Relative
    /// paths are resolved against the current working directory at startup.
    /// Ignored when <see cref="Provider"/> is <see cref="DatabaseProvider.Postgres"/>.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>SQLite only. SQLite database file name. Ignored when
    /// <see cref="Provider"/> is <see cref="DatabaseProvider.Postgres"/>.</summary>
    public string FileName { get; set; } = "centrallogin.db";

    /// <summary>Postgres only. The full Npgsql connection string. Never
    /// committed — supply it via a Container App secret, environment variable
    /// or user-secrets. Unused when <see cref="Provider"/> is
    /// <see cref="DatabaseProvider.Sqlite"/>.</summary>
    public string? ConnectionString { get; set; }
}
