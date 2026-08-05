using CentralLogin.UnitTests.Fakes;
using CentralLogin.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CentralLogin.UnitTests.Web;

public sealed class AuthenticationSetupTests
{
    /// <summary>
    /// Minimal <see cref="ILoggerFactory"/> that hands out a single
    /// <see cref="CapturingLogger{T}"/> instance regardless of the requested
    /// category, so tests can assert on it without a mocking library.
    /// </summary>
    private sealed class FakeLoggerFactory : ILoggerFactory
    {
        // AuthenticationSetup is a static class and cannot be used as a
        // generic type argument; the marker type only selects the
        // CapturingLogger<T> overload and plays no other role here.
        public CapturingLogger<AuthenticationSetupTests> Logger { get; } = new();

        public ILogger CreateLogger(string categoryName) => Logger;

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private static RemoteFailureContext CreateFailureContext(Exception failure, out FakeLoggerFactory loggerFactory)
    {
        loggerFactory = new FakeLoggerFactory();

        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(loggerFactory);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };

        var scheme = new AuthenticationScheme(
            OpenIdConnectDefaults.AuthenticationScheme,
            displayName: null,
            typeof(OpenIdConnectHandler));

        return new RemoteFailureContext(httpContext, scheme, new OpenIdConnectOptions(), failure);
    }

    [Fact]
    public async Task Remote_failure_is_logged_and_still_redirects_to_the_generic_error_page()
    {
        var failure = new InvalidOperationException("Message contains error: 'access_denied'.");
        var context = CreateFailureContext(failure, out var loggerFactory);

        await AuthenticationSetup.HandleRemoteFailure(context);

        // Response only ever carries the generic destination, never the
        // provider's error detail.
        Assert.Equal("/Error?source=auth", context.HttpContext.Response.Headers.Location.ToString());
        Assert.True(context.Result?.Handled);

        // The failure reason is recorded server-side instead - asserting on
        // the exception itself, not just the constant log message, since the
        // built-in ILogger formatter used by LogWarning(ex, "...") ignores
        // the exception argument entirely.
        Assert.Contains(
            loggerFactory.Logger.Entries,
            e => e.Level is LogLevel.Warning or LogLevel.Error
                && e.Exception?.Message.Contains("access_denied") == true);
    }
}
