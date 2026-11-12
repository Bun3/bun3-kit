using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Bun3.Unity.Core.Serialization
{
    /// <summary>
    /// Serializable reference to a Unity object that is consumed through the interface <typeparamref name="T"/>.
    /// Lets an assembly reference an implementation it cannot name, such as a view assigned in the inspector.
    /// </summary>
    /// <typeparam name="T">Interface (or base class) the referenced object must implement.</typeparam>
    [Serializable]
    public struct InterfaceRef<T> where T : class
    {
        [SerializeField] private Object target;

        /// <summary>Creates a reference to <paramref name="target"/>; a value not implementing <typeparamref name="T"/> yields a null <see cref="Value"/>.</summary>
        /// <param name="target">Object implementing <typeparamref name="T"/>.</param>
        public InterfaceRef(Object target)
        {
            this.target = target;
        }

        /// <summary>The referenced object as <typeparamref name="T"/>; null when unassigned, destroyed, or not a <typeparamref name="T"/>.</summary>
        public T Value => target == null ? null : target as T;

        /// <summary>
        /// Maps an object dropped in the inspector to the object to store: itself when it implements <typeparamref name="T"/>,
        /// the first implementing component when it is a <see cref="GameObject"/>, otherwise null.
        /// </summary>
        /// <param name="dropped">Object assigned by the user.</param>
        /// <returns>The object to serialize, or null when nothing on it implements <typeparamref name="T"/>.</returns>
        public static Object Resolve(Object dropped)
        {
            if (dropped == null) return null;
            if (dropped is T) return dropped;
            return dropped is GameObject go && go.TryGetComponent(typeof(T), out var component) ? component : null;
        }
    }
}
