using CentralLogin.Pulumi.Configuration;
using CentralLogin.Pulumi.Providers.Azure;

namespace CentralLogin.Pulumi.Providers;

/// <summary>
/// Selects the cloud provider implementation from configuration.
///
/// EXTENSION POINT: when a provider is chosen, add its case below and implement
/// <see cref="ICloudProvider"/> in a new class (e.g. AzureCloudProvider), then
/// reference the corresponding Pulumi package (Pulumi.AzureNative / Pulumi.Aws /
/// Pulumi.Gcp / Pulumi.Kubernetes) in the project file.
/// </summary>
public static class CloudProviderFactory
{
    public static ICloudProvider Create(StackConfiguration config) =>
        config.CloudProvider.ToLowerInvariant() switch
        {
            "none" => new NotConfiguredCloudProvider(),
            "azure" => new AzureCloudProvider(),

            // "aws"        => new AwsCloudProvider(),
            // "gcp"        => new GcpCloudProvider(),
            // "kubernetes" => new KubernetesCloudProvider(),

            _ => throw new NotSupportedException(
                $"Cloud provider '{config.CloudProvider}' is not implemented yet. " +
                "Implement ICloudProvider and register it in CloudProviderFactory."),
        };
}
