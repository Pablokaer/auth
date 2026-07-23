using CentralLogin.Application.Abstractions;

namespace CentralLogin.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
