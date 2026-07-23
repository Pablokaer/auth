using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Core;
using Cake.Frosting;

namespace CentralLogin.Build.Tasks;

[TaskName("Clean")]
public sealed class CleanTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.CleanDirectory(context.ArtifactsDirectory);
        context.DotNetClean(context.SolutionPath, new DotNetCleanSettings
        {
            Configuration = context.Configuration,
        });
    }
}

[TaskName("Restore")]
public sealed class RestoreTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetRestore(context.SolutionPath);
}

[TaskName("FormatCheck")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class FormatCheckTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.RunProcess("dotnet", $"format \"{context.SolutionPath}\" --verify-no-changes --no-restore");
}

[TaskName("VulnerabilityCheck")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class VulnerabilityCheckTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        var (exitCode, output) = context.RunProcessCaptured(
            "dotnet",
            $"list \"{context.SolutionPath}\" package --vulnerable --include-transitive");

        if (exitCode != 0)
        {
            throw new CakeException($"Vulnerability scan failed with exit code {exitCode}.");
        }

        var report = string.Join(System.Environment.NewLine, output);
        if (report.Contains("has the following vulnerable", StringComparison.OrdinalIgnoreCase))
        {
            throw new CakeException("Vulnerable NuGet packages detected:" + System.Environment.NewLine + report);
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(RestoreTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetBuild(context.SolutionPath, new DotNetBuildSettings
        {
            Configuration = context.Configuration,
            NoRestore = true,
        });
}
