using Microsoft.Extensions.Options;

namespace CentralLogin.IntegrationTests.Common;

/// <summary>
/// Helper for the <c>ValidateOnStart</c> suites: finds the
/// <see cref="OptionsValidationException"/> behind whatever the host throws
/// when startup validation rejects the configuration.
/// </summary>
public static class StartupValidationFailure
{
    /// <summary>
    /// Today the host lets <see cref="OptionsValidationException"/> surface
    /// directly from <c>StartupValidator</c>, so this finds it on the first
    /// iteration. The walk exists so the tests keep working if a future host
    /// version wraps startup failures instead; <see cref="AggregateException"/>
    /// is flattened because its validation exception need not be first.
    /// </summary>
    public static OptionsValidationException? Unwrap(Exception exception)
    {
        switch (exception)
        {
            case OptionsValidationException validationException:
                return validationException;
            case AggregateException aggregate:
                return aggregate.Flatten().InnerExceptions
                    .Select(Unwrap)
                    .FirstOrDefault(found => found is not null);
            default:
                return exception.InnerException is null
                    ? null
                    : Unwrap(exception.InnerException);
        }
    }
}
