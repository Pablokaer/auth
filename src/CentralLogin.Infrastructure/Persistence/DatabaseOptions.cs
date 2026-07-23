namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Configuration for where the SQLite database lives. Bound from the
/// <c>Database</c> configuration section. The data directory is intended to be
/// a persistent, writable location (a mounted volume in production).
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Directory that holds the SQLite file(s). Relative paths are
    /// resolved against the current working directory at startup.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>SQLite database file name.</summary>
    public string FileName { get; set; } = "centrallogin.db";
}
