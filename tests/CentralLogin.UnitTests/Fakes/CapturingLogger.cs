using Microsoft.Extensions.Logging;

namespace CentralLogin.UnitTests.Fakes;

/// <summary>
/// Minimal <see cref="ILogger{T}"/> that records every entry so tests can
/// assert a specific message/level was logged, without adding a mocking
/// library dependency to the test project.
/// </summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
