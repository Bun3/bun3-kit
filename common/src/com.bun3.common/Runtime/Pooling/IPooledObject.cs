using System;

namespace Bun3.Common.Pooling
{
    /// <summary>
    /// A poolable object. <see cref="IDisposable.Dispose"/> returns it to its pool;
    /// <see cref="SetPool"/> is called by the pool on every rental and is not for consumers.
    /// </summary>
    public interface IPooledObject<T> : IDisposable
    {
        void SetPool(IObjectPool<T> pool);
    }
}
