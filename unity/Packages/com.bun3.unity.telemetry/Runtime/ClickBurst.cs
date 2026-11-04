namespace Bun3.Unity.Telemetry
{
    /// <summary>Reduces fast repeated clicks on one control to the first click plus one summary line after the burst ends.</summary>
    sealed class ClickBurst
    {
        internal const double Window = 1.0;

        string path;
        double last;
        int count;

        /// <returns>True when this click is recorded now. endedPath is the burst that just ended (meaningful only for 2 or more clicks).</returns>
        internal bool Register(string clickPath, double now, out string endedPath, out int endedCount)
        {
            endedPath = null;
            endedCount = 0;
            if (path != null && path == clickPath && now - last <= Window)
            {
                count++;
                last = now;
                return false;
            }
            if (path != null && count >= 2) { endedPath = path; endedCount = count; }
            path = clickPath;
            last = now;
            count = 1;
            return true;
        }

        /// <returns>True when the summary of a finished burst (2 or more clicks) is recorded.</returns>
        internal bool Tick(double now, out string endedPath, out int endedCount)
        {
            endedPath = null;
            endedCount = 0;
            if (path == null || now - last <= Window) return false;
            var summarize = count >= 2;
            if (summarize) { endedPath = path; endedCount = count; }
            path = null;
            count = 0;
            return summarize;
        }
    }
}
