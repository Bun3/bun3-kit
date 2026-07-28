namespace Bun3.Common.Pooling
{
    /// <summary>An object pool that pooled objects return themselves to on dispose.</summary>
    public interface IObjectPool<T>
    {
        /// <summary>
        /// Items whose element count exceeds this at dispose time are dropped instead of
        /// pooled, so one oversized use cannot pin a large backing array forever. Read by
        /// the pooled wrapper in <c>Dispose</c>, before it clears itself.
        /// </summary>
        int MaxRetainedCount { get; }

        T Get();

        void Release(T item);
    }
}
