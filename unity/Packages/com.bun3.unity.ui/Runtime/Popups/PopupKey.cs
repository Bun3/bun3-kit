using System;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Popup identity key. The default convention is <b>the popup type itself is the key</b> —
    /// <see cref="Of{TPopup}"/> uses the class name; only variants opening different prefabs
    /// with the same class specify a name. Server/table-data paths create keys via the implicit
    /// string conversion.
    /// </summary>
    /// <remarks>
    /// Equality uses only <see cref="Name"/> (ordinal) — a data-path request with the same name
    /// correctly counts as a duplicate of a type-path popup. Factories may use <see cref="Name"/>
    /// directly as the load address (Addressables/Resources key). Since the class name is the
    /// identifier, avoid same-named popup classes that differ only by namespace.
    /// Comparison is allocation-free; type-key names are interned once at startup via a generic static cache.
    /// </remarks>
    public readonly struct PopupKey : IEquatable<PopupKey>
    {
        /// <summary>Unique identifier — usually the popup class name, or the given name for variant prefabs. Equality basis.</summary>
        public readonly string Name;

        /// <summary>Popup type that created the key (type path only). Metadata; not part of equality.</summary>
        public readonly Type PopupType;

        /// <summary>Data-path constructor — key from a name only.</summary>
        public PopupKey(string name) : this(name, null) { }

        /// <summary>Key from name + type metadata. Usually use <see cref="Of{TPopup}"/> instead.</summary>
        public PopupKey(string name, Type popupType)
        {
            Name = name;
            PopupType = popupType;
        }

        /// <summary>
        /// Uses the type as the key (default convention). Pass <paramref name="popupName"/> to
        /// identify a variant prefab of the same class as a separate popup.
        /// </summary>
        public static PopupKey Of<TPopup>(string popupName = null) where TPopup : Popup
            => new(popupName ?? TypeName<TPopup>.Value, typeof(TPopup));

        // One-time startup interning of type-key names.
        private static class TypeName<TPopup> where TPopup : Popup
        {
            internal static readonly string Value = typeof(TPopup).Name;
        }

        /// <summary>Implicit conversion to accept popup names from server/table data directly.</summary>
        public static implicit operator PopupKey(string name) => new(name);

        /// <summary>Name equality (ordinal). Allocation-free.</summary>
        public bool Equals(PopupKey other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        /// <summary>Equality against a boxed <see cref="PopupKey"/>.</summary>
        public override bool Equals(object obj) => obj is PopupKey other && Equals(other);

        /// <summary>Ordinal hash of the name.</summary>
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);

        /// <summary>Name equality.</summary>
        public static bool operator ==(PopupKey left, PopupKey right) => left.Equals(right);

        /// <summary>Name inequality.</summary>
        public static bool operator !=(PopupKey left, PopupKey right) => !left.Equals(right);

        /// <summary>Key name, for debug display.</summary>
        public override string ToString() => Name ?? string.Empty;
    }
}
