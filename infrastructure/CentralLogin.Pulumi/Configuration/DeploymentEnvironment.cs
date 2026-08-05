namespace CentralLogin.Pulumi.Configuration;

public enum DeploymentEnvironment
{
    Development,
    Staging,
    Production,
}

public static class DeploymentEnvironmentParser
{
    /// <summary>
    /// The complete list of Pulumi stack names accepted by <see cref="FromStackName"/>
    /// (matched case-insensitively). There is no fallback beyond this list, so it must
    /// be updated whenever a new alias is intentionally supported.
    /// </summary>
    public static IReadOnlyCollection<string> KnownStackNames { get; } =
    [
        "dev", "development",
        "staging",
        "prod", "production",
    ];

    /// <summary>
    /// Maps a Pulumi stack name to a <see cref="DeploymentEnvironment"/>. Matching is
    /// case-insensitive and the input is trimmed, but otherwise exact: accepted names
    /// are <c>dev</c>/<c>development</c>, <c>staging</c>, and <c>prod</c>/<c>production</c>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="stackName"/> is null, empty, whitespace, or not one of
    /// <see cref="KnownStackNames"/>. A stack name is never inferred - an unrecognised
    /// name must fail fast rather than silently provision as <see cref="DeploymentEnvironment.Development"/>.
    /// </exception>
    public static DeploymentEnvironment FromStackName(string stackName)
    {
        var trimmed = stackName?.Trim();

        return trimmed?.ToLowerInvariant() switch
        {
            "dev" or "development" => DeploymentEnvironment.Development,
            "staging" => DeploymentEnvironment.Staging,
            "prod" or "production" => DeploymentEnvironment.Production,
            _ => throw new ArgumentException(
                $"Unrecognised Pulumi stack name '{stackName ?? "<null>"}'. Accepted values (case-insensitive): "
                + $"{string.Join(", ", KnownStackNames)}.",
                nameof(stackName)),
        };
    }
}
