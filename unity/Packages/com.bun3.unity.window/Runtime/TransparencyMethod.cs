namespace Bun3.Unity.Window
{
    /// <summary>The transparency mechanism actually active on the window.</summary>
    public enum TransparencyMethod
    {
        /// <summary>Not applied (never requested, failed, or unsupported platform).</summary>
        None = 0,

        /// <summary>
        /// DWM composition (<c>DwmExtendFrameIntoClientArea</c>): per-pixel alpha,
        /// smooth edges. Preferred.
        /// </summary>
        Dwm = 1,

        /// <summary>
        /// Layered-window color key (<c>SetLayeredWindowAttributes</c>): pixels matching
        /// the key color become fully transparent. Binary cutout, no partial alpha;
        /// works where DWM alpha fails.
        /// </summary>
        ColorKey = 2,
    }

    /// <summary>Which transparency mechanism <see cref="WindowTransparency.Apply"/> should try.</summary>
    public enum TransparencyPreference
    {
        /// <summary>Try DWM first, fall back to color key on failure.</summary>
        Auto = 0,

        /// <summary>Skip DWM and use the color key directly.</summary>
        ColorKey = 2,
    }
}
