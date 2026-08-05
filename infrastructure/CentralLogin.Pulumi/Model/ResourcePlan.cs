using CentralLogin.Pulumi.Configuration;

namespace CentralLogin.Pulumi.Model;

/// <summary>
/// Provider-neutral description of the infrastructure CentralLogin needs. This
/// is the contract each cloud provider implementation fulfils. It intentionally
/// contains no cloud SDK types — only intent and configuration. Concrete
/// providers translate this plan into real resources at their extension points.
/// </summary>
public sealed record ResourcePlan(
    DeploymentEnvironment Environment,
    ApplicationHostingPlan ApplicationHosting,
    KeycloakHostingPlan KeycloakHosting,
    PostgresPlan KeycloakDatabase,
    ContainerRegistryPlan ContainerRegistry,
    DnsPlan Dns,
    TlsPlan Tls,
    SecretManagementPlan SecretManagement,
    LoggingPlan Logging,
    MonitoringPlan Monitoring,
    BackupPlan Backups,
    PortalsPlan Portals)
{
    public static ResourcePlan FromConfiguration(StackConfiguration config)
    {
        var isProduction = config.Environment == DeploymentEnvironment.Production;

        // Portal URLs are account-specific and never invented — except for this
        // one documented default: when an explicit URL isn't configured but a
        // custom domain is, derive a conventional subdomain from it so the Web
        // app still gets a real (non-placeholder) value to redirect to.
        string? DefaultPortalUrl(string subdomain) =>
            config.Domain is { Length: > 0 } domain ? $"https://{subdomain}.{domain}" : null;

        return new ResourcePlan(
            Environment: config.Environment,
            ApplicationHosting: new ApplicationHostingPlan(
                MinInstances: isProduction ? 2 : 1,
                MaxInstances: isProduction ? 10 : 2,
                PublicHostname: config.Domain),
            KeycloakHosting: new KeycloakHostingPlan(
                Image: "quay.io/keycloak/keycloak:26.5.6",
                StartCommand: "start", // production uses `start`, never `start-dev`
                AdminConsolePubliclyExposed: false),
            KeycloakDatabase: new PostgresPlan(
                Image: "postgres:17.6",
                HighAvailability: isProduction,
                // Azure Database for PostgreSQL Flexible Server only accepts
                // storage sizes from a fixed set whose minimum is 32 GiB.
                StorageSizeGigabytes: isProduction ? 128 : 32),
            ContainerRegistry: new ContainerRegistryPlan(
                Repository: "centrallogin-web"),
            Dns: new DnsPlan(
                Hostname: config.Domain),
            Tls: new TlsPlan(
                ManagedCertificate: true),
            SecretManagement: new SecretManagementPlan(
                UseCloudSecretManager: true),
            Logging: new LoggingPlan(
                RetentionDays: isProduction ? 90 : 14),
            Monitoring: new MonitoringPlan(
                AlertingEnabled: isProduction),
            Backups: new BackupPlan(
                Enabled: isProduction,
                RetentionDays: isProduction ? 30 : 7),
            Portals: new PortalsPlan(
                AdminUrl: config.AdminPortalUrl ?? DefaultPortalUrl("admin"),
                EmployeeUrl: config.EmployeePortalUrl ?? DefaultPortalUrl("employee")));
    }
}

public sealed record ApplicationHostingPlan(int MinInstances, int MaxInstances, string? PublicHostname);
public sealed record KeycloakHostingPlan(string Image, string StartCommand, bool AdminConsolePubliclyExposed);
public sealed record PostgresPlan(string Image, bool HighAvailability, int StorageSizeGigabytes);
public sealed record ContainerRegistryPlan(string Repository);
public sealed record DnsPlan(string? Hostname);
public sealed record TlsPlan(bool ManagedCertificate);
public sealed record SecretManagementPlan(bool UseCloudSecretManager);
public sealed record LoggingPlan(int RetentionDays);
public sealed record MonitoringPlan(bool AlertingEnabled);
public sealed record BackupPlan(bool Enabled, int RetentionDays);
public sealed record PortalsPlan(string? AdminUrl, string? EmployeeUrl);
