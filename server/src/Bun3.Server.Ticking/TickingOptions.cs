using System;

namespace Bun3.Server.Ticking
{
    /// <summary>TickLoop behavior options — snapshotted in the constructor; later changes are ignored.</summary>
    public sealed class TickingOptions
    {
        /// <summary>Target period of one loop revolution. Job execution time is subtracted from the wait (drift compensation).</summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromMilliseconds(100);

        /// <summary>Clock — defaults to the system clock. Replaceable for tests/special environments.</summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    }
}
