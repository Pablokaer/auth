using CentralLogin.Pulumi.Configuration;
using CentralLogin.Pulumi.Model;
using Pulumi;

namespace CentralLogin.Pulumi.Providers;

/// <summary>
/// Default provider used while scaffolding. It creates NO resources, so
/// <c>pulumi up</c> is safe and idempotent until a real provider is selected.
/// It emits a warning and a descriptive (non-sensitive) output only.
/// </summary>
public sealed class NotConfiguredCloudProvider : ICloudProvider
{
    public string Name => "none";

    public InfrastructureOutputs Provision(StackConfiguration config, ResourcePlan plan)
    {
        Log.Warn(
            "No cloud provider is selected (central-login:cloudProvider = 'none'). " +
            "No resources will be created. Set a provider and implement its extension " +
            "point in Providers/ to provision infrastructure.");

        return new InfrastructureOutputs
        {
            Notes = $"Scaffold only. Environment='{config.Environment}', provider='none'. "
                    + "No cloud resources were created.",
        };
    }
}
