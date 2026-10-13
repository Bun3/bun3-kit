namespace Bun3.State
{
    /// <summary>Exposes stable metadata and directly owned children of a state or tracked list.</summary>
    public interface ITrackedNode
    {
        /// <summary>Gets stable tracking metadata without initializing child ownership.</summary>
        StateNode Tracking { get; }

        /// <summary>Visits every nonnull directly owned mutable child, without enumerator allocation or side effects.</summary>
        /// <remarks>Read backing fields directly; do not invoke child initialization during visitation.</remarks>
        /// <param name="visitor">The visitor that receives each child.</param>
        void VisitChildren(IStateChildVisitor visitor);
    }
}
