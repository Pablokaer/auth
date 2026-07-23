using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Test;
using Cake.Frosting;

namespace CentralLogin.Build.Tasks;

[TaskName("UnitTests")]
[IsDependentOn(typeof(BuildTask))]
public sealed class UnitTestsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetTest("tests/CentralLogin.UnitTests/CentralLogin.UnitTests.csproj", Settings(context));

    internal static DotNetTestSettings Settings(BuildContext context) => new()
    {
        Configuration = context.Configuration,
        NoBuild = true,
        NoRestore = true,
    };
}

[TaskName("IntegrationTests")]
[IsDependentOn(typeof(BuildTask))]
public sealed class IntegrationTestsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetTest(
            "tests/CentralLogin.IntegrationTests/CentralLogin.IntegrationTests.csproj",
            UnitTestsTask.Settings(context));
}

[TaskName("ArchitectureTests")]
[IsDependentOn(typeof(BuildTask))]
public sealed class ArchitectureTestsTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetTest(
            "tests/CentralLogin.ArchitectureTests/CentralLogin.ArchitectureTests.csproj",
            UnitTestsTask.Settings(context));
}
