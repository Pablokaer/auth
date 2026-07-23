using CentralLogin.Pulumi.Configuration;
using CentralLogin.Pulumi.Model;

namespace CentralLogin.Pulumi.Providers;

/// <summary>
/// A cloud provider turns a provider-neutral <see cref="ResourcePlan"/> into
/// real Pulumi resources. This is the single extension point to implement when
/// a target provider is chosen (Azure, AWS, GCP, Kubernetes, ...).
/// </summary>
public interface ICloudProvider
{
    string Name { get; }

    InfrastructureOutputs Provision(StackConfiguration config, ResourcePlan plan);
}
