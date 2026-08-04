using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CentralLogin.UnitTests.Fakes;

/// <summary>Minimal <see cref="IHostEnvironment"/> so tests can pick an environment name without spinning up a host.</summary>
public sealed class FakeHostEnvironment : IHostEnvironment
{
    public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "CentralLogin.UnitTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
