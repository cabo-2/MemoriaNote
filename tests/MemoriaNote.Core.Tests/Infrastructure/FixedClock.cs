namespace MemoriaNote.Core.Tests.Infrastructure;

internal sealed class FixedClock : IClock
{
    internal FixedClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
