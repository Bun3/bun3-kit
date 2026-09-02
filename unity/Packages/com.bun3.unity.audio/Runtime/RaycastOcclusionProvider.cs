using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Default occlusion strategy: a single physics linecast from listener to source.
    /// Binary verdict (blocked = 1, clear = 0); the voice-side smoothing turns it into
    /// a soft transition. Wall thickness and material are ignored.
    /// </summary>
    public sealed class RaycastOcclusionProvider : IOcclusionProvider
    {
        /// <summary>Layers treated as sound obstructions.</summary>
        public LayerMask ObstructionMask;

        /// <summary>Creates a provider testing against the given obstruction layers.</summary>
        public RaycastOcclusionProvider(LayerMask obstructionMask)
        {
            ObstructionMask = obstructionMask;
        }

        /// <inheritdoc/>
        public float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos)
            => Physics.Linecast(listenerPos, sourcePos, ObstructionMask) ? 1f : 0f;
    }
}
