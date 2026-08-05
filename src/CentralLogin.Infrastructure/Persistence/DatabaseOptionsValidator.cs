using Microsoft.Extensions.Options;

namespace CentralLogin.Infrastructure.Persistence;

/// <summary>
/// Fails fast at startup (via <c>ValidateOnStart</c>) when <see cref="DatabaseOptions"/>
/// does not carry what its selected <see cref="DatabaseOptions.Provider"/> needs:
/// <see cref="DatabaseProvider.Postgres"/> requires a connection string, and
/// <see cref="DatabaseProvider.Sqlite"/> requires a data directory and file
/// name. Without this, a deployment that switches <c>Database:Provider</c> to
/// <c>Postgres</c> but forgets <c>Database:ConnectionString</c> would only
/// fail once EF Core tries to open a connection — on the first request rather
/// than while the host is starting.
/// </summary>
public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var failures = new List<string>();

        switch (options.Provider)
        {
            case DatabaseProvider.Postgres:
                ValidateRequired(nameof(DatabaseOptions.ConnectionString), options.ConnectionString, options.Provider, failures);
                break;

            case DatabaseProvider.Sqlite:
                ValidateRequired(nameof(DatabaseOptions.DataDirectory), options.DataDirectory, options.Provider, failures);
                ValidateRequired(nameof(DatabaseOptions.FileName), options.FileName, options.Provider, failures);
                break;

            default:
                failures.Add($"{Key(nameof(DatabaseOptions.Provider))} '{options.Provider}' is not a supported provider.");
                break;
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateRequired(string property, string? value, DatabaseProvider provider, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        failures.Add($"{Key(property)} is required when {Key(nameof(DatabaseOptions.Provider))} is '{provider}'.");
    }

    private static string Key(string property) => $"{DatabaseOptions.SectionName}:{property}";
}
