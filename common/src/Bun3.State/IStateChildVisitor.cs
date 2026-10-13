namespace Bun3.State
{
    /// <summary>Receives direct children during state graph traversal.</summary>
    public interface IStateChildVisitor
    {
        /// <summary>Visits one directly owned child.</summary>
        /// <param name="child">The child to visit.</param>
        void Visit(ITrackedNode child);
    }
}
