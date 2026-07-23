using CentralLogin.Application.Abstractions;

namespace CentralLogin.UnitTests.Fakes;

public sealed class FixedClock : IClock
{
    public FixedClock(DateTime utcNow) => UtcNow = utcNow;

    public DateTime UtcNow { get; set; }
}
