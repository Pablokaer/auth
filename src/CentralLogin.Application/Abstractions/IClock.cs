namespace CentralLogin.Application.Abstractions;

/// <summary>Abstraction over the system clock so use cases are testable.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
