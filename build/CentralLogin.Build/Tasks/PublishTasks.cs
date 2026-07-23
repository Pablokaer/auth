using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Frosting;

namespace CentralLogin.Build.Tasks;

[TaskName("Publish")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PublishTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        => context.DotNetPublish(context.WebProjectPath, new DotNetPublishSettings
        {
            Configuration = context.Configuration,
            NoRestore = true,
            OutputDirectory = context.ArtifactsDirectory.Combine("web"),
        });
}

[TaskName("DockerBuild")]
[IsDependentOn(typeof(PublishTask))]
public sealed class DockerBuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
        // Uses the .NET SDK container-publishing target (Microsoft.NET.Build.Containers),
        // which builds an OCI/Docker image via the local container runtime — no Dockerfile required.
        => context.RunProcess(
            "dotnet",
            $"publish \"{context.WebProjectPath}\" -c {context.Configuration} " +
            $"/t:PublishContainer -p:ContainerRepository={context.ContainerRepository}");
}
