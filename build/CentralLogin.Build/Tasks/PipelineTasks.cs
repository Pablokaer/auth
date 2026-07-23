using Cake.Frosting;

namespace CentralLogin.Build.Tasks;

/// <summary>
/// CI aggregates every validation and test task. If any dependency fails, the
/// process exits non-zero and CD (which depends on CI) will not run.
/// </summary>
[TaskName("CI")]
[IsDependentOn(typeof(FormatCheckTask))]
[IsDependentOn(typeof(VulnerabilityCheckTask))]
[IsDependentOn(typeof(BuildTask))]
[IsDependentOn(typeof(UnitTestsTask))]
[IsDependentOn(typeof(IntegrationTestsTask))]
[IsDependentOn(typeof(ArchitectureTestsTask))]
public sealed class CiTask : FrostingTask<BuildContext>
{
}

/// <summary>
/// CD depends on CI (so it cannot deploy when validation/tests fail), then
/// publishes, builds the container and runs the Pulumi deployment.
/// </summary>
[TaskName("CD")]
[IsDependentOn(typeof(CiTask))]
[IsDependentOn(typeof(PublishTask))]
[IsDependentOn(typeof(DockerBuildTask))]
[IsDependentOn(typeof(PulumiDeployTask))]
public sealed class CdTask : FrostingTask<BuildContext>
{
}

[TaskName("Default")]
[IsDependentOn(typeof(CiTask))]
public sealed class DefaultTask : FrostingTask<BuildContext>
{
}
