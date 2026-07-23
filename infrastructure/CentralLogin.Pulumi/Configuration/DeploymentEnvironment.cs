namespace CentralLogin.Pulumi.Configuration;

public enum DeploymentEnvironment
{
    Development,
    Staging,
    Production,
}

public static class DeploymentEnvironmentParser
{
    /// <summary>Maps a Pulumi stack name (dev/staging/prod) to an environment.</summary>
    public static DeploymentEnvironment FromStackName(string stackName) => stackName.ToLowerInvariant() switch
    {
        "dev" or "development" => DeploymentEnvironment.Development,
        "staging" => DeploymentEnvironment.Staging,
        "prod" or "production" => DeploymentEnvironment.Production,
        _ => DeploymentEnvironment.Development,
    };
}
