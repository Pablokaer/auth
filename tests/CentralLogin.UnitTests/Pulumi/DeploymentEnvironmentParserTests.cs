using CentralLogin.Pulumi.Configuration;
using Xunit;

namespace CentralLogin.UnitTests.Pulumi;

public sealed class DeploymentEnvironmentParserTests
{
    [Theory]
    [InlineData("dev", DeploymentEnvironment.Development)]
    [InlineData("development", DeploymentEnvironment.Development)]
    [InlineData("staging", DeploymentEnvironment.Staging)]
    [InlineData("prod", DeploymentEnvironment.Production)]
    [InlineData("production", DeploymentEnvironment.Production)]
    public void Recognised_stack_names_map_to_the_expected_environment(string stackName, DeploymentEnvironment expected)
    {
        var result = DeploymentEnvironmentParser.FromStackName(stackName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("PROD", DeploymentEnvironment.Production)]
    [InlineData("Development", DeploymentEnvironment.Development)]
    [InlineData("StAgInG", DeploymentEnvironment.Staging)]
    public void Matching_is_case_insensitive(string stackName, DeploymentEnvironment expected)
    {
        var result = DeploymentEnvironmentParser.FromStackName(stackName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("  prod  ", DeploymentEnvironment.Production)]
    [InlineData("\tstaging\n", DeploymentEnvironment.Staging)]
    public void Surrounding_whitespace_is_trimmed(string stackName, DeploymentEnvironment expected)
    {
        var result = DeploymentEnvironmentParser.FromStackName(stackName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Prod-EU")]
    [InlineData("production-2")]
    [InlineData("prd")]
    [InlineData("stage")]
    public void Unrecognised_stack_names_are_rejected(string stackName)
    {
        var exception = Assert.Throws<ArgumentException>(() => DeploymentEnvironmentParser.FromStackName(stackName));

        Assert.Contains(stackName, exception.Message);
        foreach (var known in DeploymentEnvironmentParser.KnownStackNames)
        {
            Assert.Contains(known, exception.Message);
        }
    }

    [Fact]
    public void Empty_stack_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DeploymentEnvironmentParser.FromStackName(""));
    }

    [Fact]
    public void Whitespace_only_stack_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DeploymentEnvironmentParser.FromStackName("   "));
    }

    [Fact]
    public void Null_stack_name_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => DeploymentEnvironmentParser.FromStackName(null!));
    }
}
