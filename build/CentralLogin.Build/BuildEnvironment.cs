namespace CentralLogin.Build;

/// <summary>Deployment environments supported by the pipeline.</summary>
public enum BuildEnvironment
{
    Development,
    Staging,
    Production,
}

public static class BuildEnvironmentExtensions
{
    /// <summary>Maps an environment to its Pulumi stack name.</summary>
    public static string ToStackName(this BuildEnvironment environment) => environment switch
    {
        BuildEnvironment.Development => "dev",
        BuildEnvironment.Staging => "staging",
        BuildEnvironment.Production => "prod",
        _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, null),
    };
}
