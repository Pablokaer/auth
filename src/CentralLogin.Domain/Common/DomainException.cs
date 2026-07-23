namespace CentralLogin.Domain.Common;

/// <summary>
/// Raised when a domain invariant is violated. Domain code throws this instead
/// of framework-specific exceptions so the domain stays free of infrastructure.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
