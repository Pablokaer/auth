using System.Reflection;
using CentralLogin.Application.Users;
using CentralLogin.Domain.Users;
using NetArchTest.Rules;
using Xunit;

namespace CentralLogin.ArchitectureTests;

/// <summary>
/// Enforces the dependency rules of the architecture:
///   Domain      -> depends on nothing external
///   Application -> depends on Domain only (no EF Core / ASP.NET / infra)
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = typeof(ApplicationUser).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(UserProvisioningService).Assembly;

    private static readonly string[] ForbiddenForDomain =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Aspire",
        "Pulumi",
        "Cake",
        "CentralLogin.Application",
        "CentralLogin.Infrastructure",
        "CentralLogin.Web",
    ];

    private static readonly string[] ForbiddenForApplication =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Aspire",
        "Pulumi",
        "Cake",
        "CentralLogin.Infrastructure",
        "CentralLogin.Web",
    ];

    [Fact]
    public void Domain_has_no_forbidden_dependencies()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenForDomain)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Application_has_no_forbidden_dependencies()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(ForbiddenForApplication)
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result)
        => result.IsSuccessful
            ? "OK"
            : "Offending types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
