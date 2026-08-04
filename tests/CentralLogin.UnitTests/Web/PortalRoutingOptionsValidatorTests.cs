using CentralLogin.UnitTests.Fakes;
using CentralLogin.Web.Authentication;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class PortalRoutingOptionsValidatorTests
{
    private static PortalRoutingOptionsValidator CreateValidator(string environmentName)
        => new(new FakeHostEnvironment(environmentName));

    [Fact]
    public void Valid_https_urls_pass_outside_development()
    {
        var validator = CreateValidator(Environments.Production);

        var result = validator.Validate(
            name: null,
            new PortalRoutingOptions { AdminUrl = "https://admin.example.com", EmployeeUrl = "https://employee.example.com" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Empty_and_null_values_pass_because_portals_are_optional()
    {
        var validator = CreateValidator(Environments.Production);

        var result = validator.Validate(name: null, new PortalRoutingOptions { AdminUrl = null, EmployeeUrl = "" });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Non_absolute_url_fails()
    {
        var validator = CreateValidator(Environments.Development);

        var result = validator.Validate(name: null, new PortalRoutingOptions { AdminUrl = "/relative/path" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(PortalRoutingOptions.AdminUrl)));
    }

    [Fact]
    public void Non_http_scheme_fails()
    {
        var validator = CreateValidator(Environments.Development);

        var result = validator.Validate(name: null, new PortalRoutingOptions { EmployeeUrl = "ftp://example.com" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(PortalRoutingOptions.EmployeeUrl)));
    }

    [Fact]
    public void Http_url_fails_outside_development()
    {
        var validator = CreateValidator(Environments.Production);

        var result = validator.Validate(name: null, new PortalRoutingOptions { AdminUrl = "http://admin.example.com" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains(nameof(PortalRoutingOptions.AdminUrl)));

        // The failure must name the actual reason (https required) rather
        // than the generic "must be an absolute http/https URL" - the value
        // IS an absolute http URL, it is just the wrong scheme here.
        Assert.Contains(result.Failures!, f => f.Contains("https is required") && !f.Contains("must be an absolute"));
    }

    [Fact]
    public void Non_absolute_url_failure_names_the_generic_reason_not_https()
    {
        var validator = CreateValidator(Environments.Development);

        var result = validator.Validate(name: null, new PortalRoutingOptions { AdminUrl = "/relative/path" });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("must be an absolute") && !f.Contains("https is required"));
    }

    [Fact]
    public void Http_url_passes_in_development()
    {
        var validator = CreateValidator(Environments.Development);

        var result = validator.Validate(name: null, new PortalRoutingOptions { AdminUrl = "http://localhost:5100" });

        Assert.True(result.Succeeded);
    }
}
