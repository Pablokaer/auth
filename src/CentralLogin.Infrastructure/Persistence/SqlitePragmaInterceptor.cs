using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Applies SQLite reliability pragmas every time a connection is opened:
/// <list type="bullet">
///   <item><c>journal_mode=WAL</c> — write-ahead logging for better read/write concurrency.</item>
///   <item><c>synchronous=NORMAL</c> — safe with WAL, far faster than FULL.</item>
///   <item><c>busy_timeout=5000</c> — wait up to 5s for a lock instead of failing immediately.</item>
///   <item><c>foreign_keys=ON</c> — belt-and-braces with the connection string setting.</item>
/// </list>
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA synchronous=NORMAL;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }
}
