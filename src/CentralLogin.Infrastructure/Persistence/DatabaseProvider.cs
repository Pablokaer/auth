namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Selects which EF Core provider <see cref="DatabaseOptions"/> configures.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>Local, container-instance SQLite file. Intended for local
    /// development only — a container-local file is lost on restart and
    /// cannot be safely shared across replicas.</summary>
    Sqlite,

    /// <summary>PostgreSQL via Npgsql. Required for any deployment running
    /// more than one replica, since every replica then shares the same
    /// server instead of writing to its own ephemeral SQLite file.</summary>
    Postgres,
}
