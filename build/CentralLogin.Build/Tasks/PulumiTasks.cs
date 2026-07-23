using Cake.Core;
using Cake.Frosting;

namespace CentralLogin.Build.Tasks;

[TaskName("PulumiPreview")]
public sealed class PulumiPreviewTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.Pulumi.Preview(context.Environment.ToStackName(), context.PulumiProjectDirectory);
}

[TaskName("PulumiDeploy")]
public sealed class PulumiDeployTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        // Require an explicit environment — never fall back to a default for a deploy.
        if (!context.EnvironmentWasProvided)
        {
            throw new CakeException(
                "PulumiDeploy requires an explicit --environment=Development|Staging|Production.");
        }

        // Extra guard so production is never deployed by accident.
        if (context.Environment == BuildEnvironment.Production && !context.ConfirmProduction)
        {
            throw new CakeException(
                "Refusing to deploy to Production without --confirm-production=true.");
        }

        context.Pulumi.Up(context.Environment.ToStackName(), context.PulumiProjectDirectory);
    }
}
