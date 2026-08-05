using Pulumi;

namespace CentralLogin.Pulumi.Configuration;

/// <summary>
/// Typed view over Pulumi stack configuration. Sensitive values are read with
/// <see cref="Config.GetSecret(string)"/> so Pulumi treats them as secrets
/// (encrypted at rest and redacted in output).
/// </summary>
public sealed class StackConfiguration
{
    public required DeploymentEnvironment Environment { get; init; }
    public required string CloudProvider { get; init; }
    public string? Domain { get; init; }

    // Public portal URLs the Web app redirects authenticated accounts to.
    // Account-specific, never invented here — see ResourcePlan.PortalsPlan for
    // the one place a documented default is derived from Domain when unset.
    public string? AdminPortalUrl { get; init; }
    public string? EmployeePortalUrl { get; init; }

    // --- Secrets (marked as secret; never exported) ------------------------
    public Output<string>? KeycloakDbPassword { get; init; }
    public Output<string>? WebClientSecret { get; init; }

    public static StackConfiguration Load()
    {
        var config = new Config("central-login");

        return new StackConfiguration
        {
            Environment = DeploymentEnvironmentParser.FromStackName(Deployment.Instance.StackName),
            CloudProvider = config.Get("cloudProvider") ?? "none",
            Domain = config.Get("domain"),
            AdminPortalUrl = config.Get("adminPortalUrl"),
            EmployeePortalUrl = config.Get("employeePortalUrl"),
            KeycloakDbPassword = config.GetSecret("keycloakDbPassword"),
            WebClientSecret = config.GetSecret("webClientSecret"),
        };
    }
}
