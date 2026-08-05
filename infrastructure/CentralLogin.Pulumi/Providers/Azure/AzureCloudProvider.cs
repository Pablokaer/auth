using CentralLogin.Pulumi.Configuration;
using CentralLogin.Pulumi.Model;
using Pulumi;
using AzureNative = Pulumi.AzureNative;

namespace CentralLogin.Pulumi.Providers.Azure;

/// <summary>
/// Azure implementation of <see cref="ICloudProvider"/> using Pulumi.AzureNative.
///
/// Maps the provider-neutral <see cref="ResourcePlan"/> onto Azure resources:
///   Resource Group, Log Analytics (logging + monitoring), Azure Container
///   Registry, Key Vault (secret management, RBAC), a user-assigned managed
///   identity (lets the Container Apps read Key Vault secrets without a
///   chicken-and-egg on principal ids), PostgreSQL Flexible Server (Keycloak
///   database + backups) with the `keycloak` database and an "allow Azure
///   services" firewall rule, and a Container Apps environment hosting the
///   Web app and Keycloak — both wired with the environment/secret
///   configuration they read at runtime. The application's own `centrallogin`
///   database lives on that same PostgreSQL Flexible Server, so the "web" app
///   has no container-local file and no attached share — the Storage Account
///   an earlier revision provisioned for "persistent storage" but never
///   attached to anything has been removed. A DNS zone + custom domain/TLS
///   binding is left as a marked extension point.
///
/// Account-specific values (Azure location, tenant id, custom domain) are read
/// from stack config — never invented here. Secrets come from secret config,
/// flow through Key Vault, and are never exported. Nothing deploys until
/// <c>pulumi up</c> is run with a stack whose <c>cloudProvider</c> is <c>azure</c>.
/// </summary>
public sealed class AzureCloudProvider : ICloudProvider
{
    // Must match the realm/client baked into keycloak/realm/central-login-realm.json
    // and the defaults in CentralLogin.Web.Authentication.KeycloakAuthenticationOptions.
    private const string KeycloakRealm = "central-login";
    private const string KeycloakClientId = "central-login-web";
    private const string KeycloakDatabaseName = "keycloak";

    // Built-in Azure role "Key Vault Secrets User" (read secret contents, no management).
    private const string KeyVaultSecretsUserRoleDefinitionId = "4633458b-17de-408a-b874-0445c86b69e6";

    public string Name => "azure";

    /// <summary>Stable GUID derived from <paramref name="seed"/> (same input, same output).</summary>
    private static string DeterministicGuid(string seed)
    {
        // SHA-256 (truncated to 16 bytes for Guid), not MD5: nothing security-sensitive
        // depends on this, but MD5 trips CA5351 (broken cryptographic algorithm).
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return new Guid(hash[..16]).ToString();
    }

    public InfrastructureOutputs Provision(StackConfiguration config, ResourcePlan plan)
    {
        var app = new Config("central-login");

        var prefix = $"cl-{plan.Environment.ToString().ToLowerInvariant()}";

        // Location is account/region specific: required, not defaulted/invented.
        var location = app.Require("azureLocation");
        var tenantId = app.Require("azureTenantId");

        var keycloakDbPassword = config.KeycloakDbPassword
            ?? throw new InvalidOperationException(
                "Set secret config 'central-login:keycloakDbPassword'.");
        var webClientSecret = config.WebClientSecret
            ?? throw new InvalidOperationException(
                "Set secret config 'central-login:webClientSecret'.");

        var resourceGroup = new AzureNative.Resources.ResourceGroup($"{prefix}-rg",
            new AzureNative.Resources.ResourceGroupArgs
            {
                Location = location,
            });

        // --- Logging & monitoring: Log Analytics workspace --------------------
        var workspace = new AzureNative.OperationalInsights.Workspace($"{prefix}-logs",
            new AzureNative.OperationalInsights.WorkspaceArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new AzureNative.OperationalInsights.Inputs.WorkspaceSkuArgs
                {
                    Name = AzureNative.OperationalInsights.WorkspaceSkuNameEnum.PerGB2018,
                },
                RetentionInDays = plan.Logging.RetentionDays,
            });

        // --- Container registry ----------------------------------------------
        var registry = new AzureNative.ContainerRegistry.Registry($"{prefix}acr",
            new AzureNative.ContainerRegistry.RegistryArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new AzureNative.ContainerRegistry.Inputs.SkuArgs
                {
                    Name = AzureNative.ContainerRegistry.SkuName.Basic,
                },
                AdminUserEnabled = false,
            });

        // --- Secret management: Key Vault (RBAC, no access policies) ----------
        var keyVault = new AzureNative.KeyVault.Vault($"{prefix}-kv",
            new AzureNative.KeyVault.VaultArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Properties = new AzureNative.KeyVault.Inputs.VaultPropertiesArgs
                {
                    TenantId = tenantId,
                    Sku = new AzureNative.KeyVault.Inputs.SkuArgs
                    {
                        Family = AzureNative.KeyVault.SkuFamily.A,
                        Name = AzureNative.KeyVault.SkuName.Standard,
                    },
                    EnableRbacAuthorization = true,
                    EnableSoftDelete = true,
                },
            });

        // Secret values live only in Key Vault; the Container Apps below read
        // them via KeyVaultUrl + managed identity secret references, never as
        // literal env values, and nothing here is exported as a stack output.
        var keycloakDbPasswordSecret = new AzureNative.KeyVault.Secret($"{prefix}-kc-db-password",
            new AzureNative.KeyVault.SecretArgs
            {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "keycloak-db-password",
                Properties = new AzureNative.KeyVault.Inputs.SecretPropertiesArgs
                {
                    Value = keycloakDbPassword,
                },
            });

        var webClientSecretSecret = new AzureNative.KeyVault.Secret($"{prefix}-web-client-secret",
            new AzureNative.KeyVault.SecretArgs
            {
                ResourceGroupName = resourceGroup.Name,
                VaultName = keyVault.Name,
                SecretName = "web-client-secret",
                Properties = new AzureNative.KeyVault.Inputs.SecretPropertiesArgs
                {
                    Value = webClientSecret,
                },
            });

        // --- Managed identity shared by both Container Apps -------------------
        // User-assigned (not system-assigned): the Key Vault role assignment
        // needs a principal id up front, but a system-assigned identity's
        // principal id only exists after the app is created — which is also
        // when the app needs the secret reference. A user-assigned identity
        // exists independently, breaking that cycle.
        var appIdentity = new AzureNative.ManagedIdentity.UserAssignedIdentity($"{prefix}-app-identity",
            new AzureNative.ManagedIdentity.UserAssignedIdentityArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
            });

        var subscriptionId = AzureNative.Authorization.GetClientConfig.Invoke()
            .Apply(c => c.SubscriptionId);
        var keyVaultSecretsUserRoleId = Output.Format(
            $"/subscriptions/{subscriptionId}/providers/Microsoft.Authorization/roleDefinitions/{KeyVaultSecretsUserRoleDefinitionId}");

        var keyVaultSecretsUserAssignment = new AzureNative.Authorization.RoleAssignment(
            $"{prefix}-app-kv-secrets-user",
            new AzureNative.Authorization.RoleAssignmentArgs
            {
                // Azure requires the role assignment's name to be a GUID (unlike
                // most other resources' names); a deterministic one avoids both
                // an invalid auto-generated name and needless replacement churn
                // across `pulumi up` runs.
                RoleAssignmentName = DeterministicGuid($"{prefix}-app-kv-secrets-user"),
                Scope = keyVault.Id,
                PrincipalId = appIdentity.PrincipalId,
                PrincipalType = AzureNative.Authorization.PrincipalType.ServicePrincipal,
                RoleDefinitionId = keyVaultSecretsUserRoleId,
            });

        var appIdentityArgs = new AzureNative.App.Inputs.ManagedServiceIdentityArgs
        {
            Type = AzureNative.App.ManagedServiceIdentityType.UserAssigned,
            UserAssignedIdentities = { appIdentity.Id },
        };

        // --- PostgreSQL Flexible Server (Keycloak DB) + backups ---------------
        var postgres = new AzureNative.DBforPostgreSQL.Server($"{prefix}-kc-pg",
            new AzureNative.DBforPostgreSQL.ServerArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Version = "16",
                AdministratorLogin = "kcadmin",
                AdministratorLoginPassword = keycloakDbPassword,
                Sku = new AzureNative.DBforPostgreSQL.Inputs.SkuArgs
                {
                    Name = plan.KeycloakDatabase.HighAvailability ? "Standard_D2ds_v5" : "Standard_B1ms",
                    Tier = plan.KeycloakDatabase.HighAvailability
                        ? AzureNative.DBforPostgreSQL.SkuTier.GeneralPurpose
                        : AzureNative.DBforPostgreSQL.SkuTier.Burstable,
                },
                Storage = new AzureNative.DBforPostgreSQL.Inputs.StorageArgs
                {
                    StorageSizeGB = plan.KeycloakDatabase.StorageSizeGigabytes,
                },
                Backup = new AzureNative.DBforPostgreSQL.Inputs.BackupArgs
                {
                    BackupRetentionDays = plan.Backups.RetentionDays,
                    GeoRedundantBackup = plan.Backups.Enabled ? "Enabled" : "Disabled",
                },
            });

        var keycloakDatabase = new AzureNative.DBforPostgreSQL.Database($"{prefix}-kc-db",
            new AzureNative.DBforPostgreSQL.DatabaseArgs
            {
                ResourceGroupName = resourceGroup.Name,
                ServerName = postgres.Name,
                DatabaseName = KeycloakDatabaseName,
            });

        // The application's own database on the Flexible Server provisioned
        // above — this is what replaces the container-local SQLite file so
        // every "web" replica shares one store.
        var applicationDatabase = new AzureNative.DBforPostgreSQL.Database($"{prefix}-app-db",
            new AzureNative.DBforPostgreSQL.DatabaseArgs
            {
                ResourceGroupName = resourceGroup.Name,
                ServerName = postgres.Name,
                DatabaseName = "centrallogin",
            });

        // Connectivity tradeoff: a VNet/private endpoint would need its own
        // infrastructure subnet (a much larger change). Start/end IP 0.0.0.0 is
        // the documented Azure sentinel for "allow access from other Azure
        // services" (not the whole internet) and is the minimal way to let the
        // Container Apps environment reach the Flexible Server today. A private
        // endpoint is the hardening step once a VNet is introduced.
        var keycloakDbFirewallRule = new AzureNative.DBforPostgreSQL.FirewallRule($"{prefix}-kc-pg-allow-azure",
            new AzureNative.DBforPostgreSQL.FirewallRuleArgs
            {
                ResourceGroupName = resourceGroup.Name,
                ServerName = postgres.Name,
                StartIpAddress = "0.0.0.0",
                EndIpAddress = "0.0.0.0",
            });

        // Flexible Server requires TLS; Keycloak's JDBC URL must ask for it.
        var keycloakDbUrl = Output.Format(
            $"jdbc:postgresql://{postgres.FullyQualifiedDomainName}:5432/{KeycloakDatabaseName}?sslmode=require");

        // Built from the server FQDN + the application database name + the
        // existing administrator login, wrapped as a secret so it is never
        // logged/diffed in plaintext and never reaches InfrastructureOutputs.
        //
        // KNOWN TRADE-OFF: this reuses the server administrator ("kcadmin"),
        // so the web app's credentials can read/drop every database on this
        // server, including Keycloak's. ARM has no way to create a scoped
        // PostgreSQL role for Flexible Server, so this is a documented
        // deferral rather than an oversight - a dedicated least-privilege
        // role for the "web" app is the follow-up.
        //
        // "SSL Mode=Require" encrypts the connection but does not validate the
        // server certificate (no protection against a MITM presenting any
        // certificate); "VerifyFull" is the hardening follow-up once the
        // server's certificate/CA is available to pin against.
        var applicationDbConnectionString = Output.CreateSecret(Output.Format(
            $"Host={postgres.FullyQualifiedDomainName};Database={applicationDatabase.Name};Username=kcadmin;Password={keycloakDbPassword};SSL Mode=Require"));

        // --- Container Apps environment (hosts Web + Keycloak) ----------------
        var sharedKeys = AzureNative.OperationalInsights.GetSharedKeys.Invoke(
            new AzureNative.OperationalInsights.GetSharedKeysInvokeArgs
            {
                ResourceGroupName = resourceGroup.Name,
                WorkspaceName = workspace.Name,
            });

        var environment = new AzureNative.App.ManagedEnvironment($"{prefix}-cae",
            new AzureNative.App.ManagedEnvironmentArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                AppLogsConfiguration = new AzureNative.App.Inputs.AppLogsConfigurationArgs
                {
                    Destination = "log-analytics",
                    LogAnalyticsConfiguration = new AzureNative.App.Inputs.LogAnalyticsConfigurationArgs
                    {
                        CustomerId = workspace.CustomerId,
                        SharedKey = sharedKeys.Apply(k => k.PrimarySharedKey ?? string.Empty),
                    },
                },
            });

        // Keycloak's own ingress FQDN isn't knowable before the app exists, so it
        // must resolve its hostname from the incoming request (KC_HOSTNAME_STRICT
        // = false) rather than being told a fixed one. `Keycloak__Authority` below
        // is derived from that same ingress FQDN, so the two always agree and the
        // OIDC issuer validates. KC_HOSTNAME should only be set to Keycloak's own
        // custom domain once the DNS/TLS extension point above actually binds one
        // to this app — and `Keycloak__Authority` must be updated in lockstep at
        // that point, or the discovery/token issuer will no longer match it.
        var keycloakHostnameEnv = new AzureNative.App.Inputs.EnvironmentVarArgs
        {
            Name = "KC_HOSTNAME_STRICT",
            Value = "false",
        };

        var keycloak = new AzureNative.App.ContainerApp($"{prefix}-keycloak",
            new AzureNative.App.ContainerAppArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ManagedEnvironmentId = environment.Id,
                Identity = appIdentityArgs,
                Configuration = new AzureNative.App.Inputs.ConfigurationArgs
                {
                    Ingress = new AzureNative.App.Inputs.IngressArgs
                    {
                        External = true,
                        TargetPort = 8080,
                        Transport = AzureNative.App.IngressTransportMethod.Auto,
                    },
                    Secrets =
                    {
                        new AzureNative.App.Inputs.SecretArgs
                        {
                            Name = "keycloak-db-password",
                            KeyVaultUrl = keycloakDbPasswordSecret.Properties.Apply(p => p.SecretUri),
                            Identity = appIdentity.Id,
                        },
                    },
                },
                Template = new AzureNative.App.Inputs.TemplateArgs
                {
                    Containers =
                    {
                        new AzureNative.App.Inputs.ContainerArgs
                        {
                            Name = "keycloak",
                            Image = plan.KeycloakHosting.Image,
                            // Production start command (never start-dev).
                            Args = { "start" },
                            Env =
                            {
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_DB", Value = "postgres" },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_DB_URL", Value = keycloakDbUrl },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_DB_USERNAME", Value = "kcadmin" },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_DB_PASSWORD", SecretRef = "keycloak-db-password" },
                                // Container Apps ingress terminates TLS and forwards plain HTTP.
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_HTTP_ENABLED", Value = "true" },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_PROXY_HEADERS", Value = "xforwarded" },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_HEALTH_ENABLED", Value = "true" },
                                new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "KC_METRICS_ENABLED", Value = "true" },
                                keycloakHostnameEnv,
                            },
                            Resources = new AzureNative.App.Inputs.ContainerResourcesArgs
                            {
                                Cpu = 0.5,
                                Memory = "1Gi",
                            },
                        },
                    },
                    Scale = new AzureNative.App.Inputs.ScaleArgs
                    {
                        MinReplicas = 1,
                        MaxReplicas = 2,
                    },
                },
            },
            new CustomResourceOptions
            {
                // Must be able to read the DB password from Key Vault and reach
                // the Flexible Server before Keycloak starts.
                DependsOn = { keyVaultSecretsUserAssignment, keycloakDbFirewallRule, keycloakDatabase },
            });

        var keycloakUrl = keycloak.Configuration.Apply(c => $"https://{c!.Ingress?.Fqdn}");

        const string databaseConnectionStringSecretName = "database-connection-string";

        var webEnv = new List<AzureNative.App.Inputs.EnvironmentVarArgs>
        {
            new() { Name = "Keycloak__Authority", Value = Output.Format($"{keycloakUrl}/realms/{KeycloakRealm}") },
            new() { Name = "Keycloak__ClientId", Value = KeycloakClientId },
            new() { Name = "Keycloak__ClientSecret", SecretRef = "web-client-secret" },
            new() { Name = "Database__Provider", Value = "Postgres" },
            new() { Name = "Database__ConnectionString", SecretRef = databaseConnectionStringSecretName },
        };

        // Portal URLs are never invented for the operator (see ResourcePlan);
        // only emit them when a real, non-placeholder value is available.
        if (plan.Portals.AdminUrl is { Length: > 0 } adminUrl)
        {
            webEnv.Add(new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "Portals__AdminUrl", Value = adminUrl });
        }

        if (plan.Portals.EmployeeUrl is { Length: > 0 } employeeUrl)
        {
            webEnv.Add(new AzureNative.App.Inputs.EnvironmentVarArgs { Name = "Portals__EmployeeUrl", Value = employeeUrl });
        }

        var web = new AzureNative.App.ContainerApp($"{prefix}-web",
            new AzureNative.App.ContainerAppArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ManagedEnvironmentId = environment.Id,
                Identity = appIdentityArgs,
                Configuration = new AzureNative.App.Inputs.ConfigurationArgs
                {
                    Ingress = new AzureNative.App.Inputs.IngressArgs
                    {
                        External = true,
                        TargetPort = 8080,
                        Transport = AzureNative.App.IngressTransportMethod.Auto,
                    },
                    // The connection string is a secret reference, never a
                    // plaintext env var Value - Container Apps stores/encrypts
                    // it separately and only the SecretRef below is visible on
                    // the revision spec.
                    Secrets =
                    {
                        new AzureNative.App.Inputs.SecretArgs
                        {
                            Name = "web-client-secret",
                            KeyVaultUrl = webClientSecretSecret.Properties.Apply(p => p.SecretUri),
                            Identity = appIdentity.Id,
                        },
                        new AzureNative.App.Inputs.SecretArgs
                        {
                            Name = databaseConnectionStringSecretName,
                            Value = applicationDbConnectionString,
                        },
                    },
                },
                Template = new AzureNative.App.Inputs.TemplateArgs
                {
                    Containers =
                    {
                        new AzureNative.App.Inputs.ContainerArgs
                        {
                            Name = "web",
                            // Image is pushed to the ACR by the pipeline; tag supplied at deploy time.
                            Image = Output.Format($"{registry.LoginServer}/{plan.ContainerRegistry.Repository}:latest"),
                            Env = webEnv,
                            Resources = new AzureNative.App.Inputs.ContainerResourcesArgs
                            {
                                Cpu = 0.5,
                                Memory = "1Gi",
                            },
                        },
                    },
                    Scale = new AzureNative.App.Inputs.ScaleArgs
                    {
                        // MinReplicas = 2 in production is safe here: every
                        // replica talks to the same PostgreSQL Flexible Server
                        // (see applicationDbConnectionString above) instead of
                        // maintaining its own container-local SQLite file, so
                        // there is no split-brain user projection and nothing
                        // is lost when a replica restarts.
                        MinReplicas = plan.ApplicationHosting.MinInstances,
                        MaxReplicas = plan.ApplicationHosting.MaxInstances,
                    },
                },
            },
            new CustomResourceOptions
            {
                // Must be able to read the client secret from Key Vault, and
                // Pulumi would otherwise create the firewall rule and the
                // application database concurrently with this Container App:
                // on a first `up` the web app can start, fail MigrateAsync
                // against an unreachable/nonexistent server and crash-loop the
                // initial revision.
                DependsOn = { keyVaultSecretsUserAssignment, keycloakDbFirewallRule, applicationDatabase },
            });

        // --- DNS + TLS (account-specific extension point) ---------------------
        // EXTENSION POINT: create the DNS zone/records (AzureNative.Network) and
        // bind the custom domain + managed TLS certificate to the Web container
        // app once domain ownership is validated. This depends on the operator's
        // verified domain and delegation, so it is intentionally not automated
        // here. `plan.Dns.Hostname` (central-login:domain) drives it when added.

        // --- Non-sensitive outputs only --------------------------------------
        return new InfrastructureOutputs
        {
            // web.Configuration carries the database connection string as a
            // secret entry Value, and Pulumi copies input secretness onto
            // same-named outputs, so
            // web.Configuration.Apply(...) would export a "[secret]" URL even
            // though the FQDN itself is not sensitive. Output.Unsecret strips
            // that inherited secretness back off before it becomes a stack output.
            ApplicationUrl = Output.Unsecret(web.Configuration).Apply(c =>
                $"https://{c!.Ingress?.Fqdn}"),
            KeycloakUrl = keycloakUrl,
            Notes = $"Azure environment '{plan.Environment}'. Registry, Key Vault, PostgreSQL "
                    + "(with the keycloak and centrallogin databases and an Azure-services "
                    + "firewall rule) and both Container Apps (wired to the database and Key "
                    + "Vault secrets) are provisioned. Left to complete: push the web image to "
                    + "ACR, and bind a custom domain + managed TLS certificate (a private "
                    + "endpoint for PostgreSQL is a further hardening step over the current "
                    + "firewall rule).",
        };
    }
}
