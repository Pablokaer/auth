using CentralLogin.Pulumi.Configuration;
using CentralLogin.Pulumi.Model;
using CentralLogin.Pulumi.Providers;
using Pulumi;

// =====================================================================
// CentralLogin infrastructure entry point (provider-neutral).
// Scaffolding creates NO resources until a cloud provider is selected and
// its ICloudProvider implementation is added. Only non-sensitive values are
// exported as stack outputs.
// =====================================================================
return await Deployment.RunAsync(() =>
{
    var config = StackConfiguration.Load();
    var plan = ResourcePlan.FromConfiguration(config);

    var provider = CloudProviderFactory.Create(config);
    var outputs = provider.Provision(config, plan);

    return outputs.ToDictionary();
});
