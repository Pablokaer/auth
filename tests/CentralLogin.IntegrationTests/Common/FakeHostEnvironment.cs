using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CentralLogin.IntegrationTests.Common;

/// <summary>Minimal <see cref="IHostEnvironment"/> so a test can compose services without a real host.</summary>
public sealed class FakeHostEnvironment : IHostEnvironment
{
    public FakeHostEnvironment(string environmentName) => EnvironmentName = environmentName;

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } = "CentralLogin.IntegrationTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
