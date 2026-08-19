#nullable enable
namespace Bun3.Gameplay.Attributes
{
    /// <summary>Minimum state a modifier-supplying source (e.g. an EffectInstance) exposes to aggregation.</summary>
    public interface IAttributeModifierSource
    {
        /// <summary>Monotonically increasing id issued by the World — the basis of canonical aggregation order.</summary>
        ulong Id { get; }

        /// <summary>Current stack count.</summary>
        int Stack { get; }

        /// <summary>Ongoing condition toggle state. When false the source is skipped during aggregation.</summary>
        bool Enabled { get; }
    }
}
