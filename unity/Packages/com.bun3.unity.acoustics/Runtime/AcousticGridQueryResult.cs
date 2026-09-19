namespace Bun3.Unity.Acoustics
{
    /// <summary>Connectivity within the supplied finite grid, not native acoustic reachability.</summary>
    public enum AcousticGridReachability
    {
        /// <summary>At least one endpoint lies outside valid walkable grid space.</summary>
        InvalidEndpoint,
        /// <summary>A four-neighbor walkable route exists within the grid.</summary>
        Connected,
        /// <summary>No four-neighbor route exists within the grid.</summary>
        Disconnected
    }

    /// <summary>Grid evidence for an endpoint's attachment to supplied probe spheres.</summary>
    public enum AcousticEndpointCoverageStatus
    {
        /// <summary>The endpoint is outside valid walkable grid space.</summary>
        InvalidEndpoint,
        /// <summary>No containing probe is visible under the grid model.</summary>
        None,
        /// <summary>Grid-visible attachment survives selection of min(containing count, limit) candidates.</summary>
        GridVisible,
        /// <summary>A visible candidate exists but bounded selection could exclude every visible candidate.</summary>
        SelectionUncertain
    }

    /// <summary>A copied endpoint result with no borrowed buffers or native visibility guarantee.</summary>
    public readonly struct AcousticEndpointCoverage
    {
        /// <summary>Grid coverage classification.</summary>
        public AcousticEndpointCoverageStatus Status { get; }
        /// <summary>First grid-visible supplied probe index, or minus one; native selection may differ.</summary>
        public int ProbeIndex { get; }
        /// <summary>Number of supplied influence spheres containing the endpoint.</summary>
        public int ContainingProbeCount { get; }
        /// <summary>Number of containing probes visible through the current grid.</summary>
        public int VisibleProbeCount { get; }

        internal AcousticEndpointCoverage(AcousticEndpointCoverageStatus status, int probeIndex, int containing, int visible)
        {
            Status = status; ProbeIndex = probeIndex;
            ContainingProbeCount = containing; VisibleProbeCount = visible;
        }
    }

    /// <summary>A stable snapshot of topology and endpoint coverage at one grid revision.</summary>
    public readonly struct AcousticGridQueryResult
    {
        /// <summary>Grid revision used by every field in this result.</summary>
        public ulong Revision { get; }
        /// <summary>Connectivity independent of endpoint probe coverage.</summary>
        public AcousticGridReachability Reachability { get; }
        /// <summary>Source attachment evidence.</summary>
        public AcousticEndpointCoverage SourceCoverage { get; }
        /// <summary>Listener attachment evidence.</summary>
        public AcousticEndpointCoverage ListenerCoverage { get; }

        internal AcousticGridQueryResult(ulong revision, AcousticGridReachability reachability,
            AcousticEndpointCoverage source, AcousticEndpointCoverage listener)
        {
            Revision = revision; Reachability = reachability; SourceCoverage = source; ListenerCoverage = listener;
        }
    }
}
