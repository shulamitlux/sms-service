namespace SmsService.Tests.Infrastructure;

/// <summary>A controllable clock, so retry/backoff tests don't need to wait.</summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; private set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}
