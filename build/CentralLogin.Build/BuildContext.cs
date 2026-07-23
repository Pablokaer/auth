using Cake.Common;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;
using CentralLogin.Build.Infrastructure;

namespace CentralLogin.Build;

/// <summary>
/// Strongly-typed build context. All pipeline parameters are read once here and
/// exposed as typed properties to the tasks.
/// </summary>
public sealed class BuildContext : FrostingContext
{
    public string Configuration { get; }
    public BuildEnvironment Environment { get; }
    public bool EnvironmentWasProvided { get; }
    public bool ConfirmProduction { get; }

    public string SolutionPath { get; } = "CentralLogin.sln";
    public string WebProjectPath { get; } = "src/CentralLogin.Web/CentralLogin.Web.csproj";
    public string PulumiProjectDirectory { get; } = "infrastructure/CentralLogin.Pulumi";
    public DirectoryPath ArtifactsDirectory { get; } = "artifacts";
    public string ContainerRepository { get; }

    public IPulumiRunner Pulumi { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        Configuration = context.Argument("configuration", "Release");
        EnvironmentWasProvided = context.HasArgument("environment");
        Environment = ParseEnvironment(context.Argument("environment", "Development"));
        ConfirmProduction = context.Argument("confirm-production", false);
        ContainerRepository = context.Argument("container-repository", "centrallogin-web");

        Pulumi = new PulumiCliRunner(this);
    }

    private static BuildEnvironment ParseEnvironment(string value) =>
        Enum.TryParse<BuildEnvironment>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new CakeException(
                $"Unknown environment '{value}'. Use Development, Staging or Production.");

    /// <summary>Runs a process and throws a <see cref="CakeException"/> on a non-zero exit code.</summary>
    public void RunProcess(string tool, string arguments, DirectoryPath? workingDirectory = null)
    {
        var settings = new ProcessSettings { Arguments = arguments };
        if (workingDirectory is not null)
        {
            settings.WorkingDirectory = workingDirectory;
        }

        var exitCode = this.StartProcess(tool, settings);
        if (exitCode != 0)
        {
            throw new CakeException($"'{tool} {arguments}' failed with exit code {exitCode}.");
        }
    }

    /// <summary>Runs a process and returns its exit code and captured stdout.</summary>
    public (int ExitCode, IReadOnlyList<string> Output) RunProcessCaptured(string tool, string arguments)
    {
        var settings = new ProcessSettings
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
        };

        var exitCode = this.StartProcess(tool, settings, out var output);
        return (exitCode, output.ToList());
    }
}
