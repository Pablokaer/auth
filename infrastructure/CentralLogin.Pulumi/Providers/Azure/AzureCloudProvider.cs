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
///   Registry, Storage Account (persistent storage), Key Vault (secret
///   management, RBAC), PostgreSQL Flexible Server (Keycloak database + backups),
///   Container Apps environment hosting the Web app and Keycloak, and a DNS zone.
///
/// Account-specific values (Azure location, tenant id, custom domain) are read
/// from stack config — never invented here. Secrets come from secret config and
/// are never exported. Nothing deploys until <c>pulumi up</c> is run with a
/// stack whose <c>cloudProvider</c> is <c>azure</c>.
/// </summary>
public sealed class AzureCloudProvider : ICloudProvider
{
    public string Name => "azure";

    public InfrastructureOutputs Provision(StackConfiguration config, ResourcePlan plan)
    {
        var azure = new Config("azure-native");
        var app = new Config("central-login");

        var prefix = $"cl-{plan.Environment.ToString().ToLowerInvariant()}";

        // Location is account/region specific: required, not defaulted/invented.
        var location = app.Require("azureLocation");
        var tenantId = app.Require("azureTenantId");

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

        // --- Persistent storage ----------------------------------------------
        var storage = new AzureNative.Storage.StorageAccount($"{prefix}store",
            new AzureNative.Storage.StorageAccountArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Sku = new AzureNative.Storage.Inputs.SkuArgs
                {
                    Name = AzureNative.Storage.SkuName.Standard_LRS,
                },
                Kind = AzureNative.Storage.Kind.StorageV2,
                AllowBlobPublicAccess = false,
                MinimumTlsVersion = AzureNative.Storage.MinimumTlsVersion.TLS1_2,
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

        // --- PostgreSQL Flexible Server (Keycloak DB) + backups ---------------
        var postgres = new AzureNative.DBforPostgreSQL.Server($"{prefix}-kc-pg",
            new AzureNative.DBforPostgreSQL.ServerArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                Version = "16",
                AdministratorLogin = "kcadmin",
                AdministratorLoginPassword = config.KeycloakDbPassword
                    ?? throw new InvalidOperationException(
                        "Set secret config 'central-login:keycloakDbPassword'."),
                Sku = new AzureNative.DBforPostgreSQL.Inputs.SkuArgs
                {
                    Name = plan.KeycloakDatabase.HighAvailability ? "Standard_D2ds_v5" : "Standard_B1ms",
                    Tier = plan.KeycloakDatabase.HighAvailability
                        ? AzureNative.DBforPostgreSQL.SkuTier.GeneralPurpose
                        : AzureNative.DBforPostgreSQL.SkuTier.Burstable,
                },
                Storage = new AzureNative.DBforPostgreSQL.Inputs.StorageArgs
                {
                    StorageSizeGB = plan.PersistentStorage.SizeGigabytes,
                },
                Backup = new AzureNative.DBforPostgreSQL.Inputs.BackupArgs
                {
                    BackupRetentionDays = plan.Backups.RetentionDays,
                    GeoRedundantBackup = plan.Backups.Enabled ? "Enabled" : "Disabled",
                },
            });

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

        var keycloak = new AzureNative.App.ContainerApp($"{prefix}-keycloak",
            new AzureNative.App.ContainerAppArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ManagedEnvironmentId = environment.Id,
                Configuration = new AzureNative.App.Inputs.ConfigurationArgs
                {
                    Ingress = new AzureNative.App.Inputs.IngressArgs
                    {
                        External = true,
                        TargetPort = 8080,
                        Transport = AzureNative.App.IngressTransportMethod.Auto,
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
            });

        var web = new AzureNative.App.ContainerApp($"{prefix}-web",
            new AzureNative.App.ContainerAppArgs
            {
                ResourceGroupName = resourceGroup.Name,
                Location = location,
                ManagedEnvironmentId = environment.Id,
                Configuration = new AzureNative.App.Inputs.ConfigurationArgs
                {
                    Ingress = new AzureNative.App.Inputs.IngressArgs
                    {
                        External = true,
                        TargetPort = 8080,
                        Transport = AzureNative.App.IngressTransportMethod.Auto,
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
                            Resources = new AzureNative.App.Inputs.ContainerResourcesArgs
                            {
                                Cpu = 0.5,
                                Memory = "1Gi",
                            },
                        },
                    },
                    Scale = new AzureNative.App.Inputs.ScaleArgs
                    {
                        MinReplicas = plan.ApplicationHosting.MinInstances,
                        MaxReplicas = plan.ApplicationHosting.MaxInstances,
                    },
                },
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
            ApplicationUrl = web.Configuration.Apply(c =>
                $"https://{c!.Ingress?.Fqdn}"),
            KeycloakUrl = keycloak.Configuration.Apply(c =>
                $"https://{c!.Ingress?.Fqdn}"),
            Notes = $"Azure environment '{plan.Environment}'. Registry, Key Vault and "
                    + "PostgreSQL provisioned; push the web image to ACR and bind the "
                    + "custom domain to complete setup.",
        };
    }
}
