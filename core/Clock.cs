using System;

namespace MemoriaNote
{
    /// <summary>
    /// Provides the current UTC time to application and infrastructure services.
    /// </summary>
    public interface IClock
    {
        /// <summary>Gets the current UTC time.</summary>
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>Provides time from the operating system clock.</summary>
    public sealed class SystemClock : IClock
    {
        /// <summary>Gets the shared stateless system clock.</summary>
        public static SystemClock Instance { get; } = new SystemClock();

        SystemClock()
        {
        }

        /// <inheritdoc/>
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
