using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Occlusion evaluation strategy. Returns 0 for fully open, 1 for fully occluded;
    /// intermediate values are allowed. Called from the sound system's tick on a
    /// round-robin budget — implementations must not allocate.
    /// </summary>
    public interface IOcclusionProvider
    {
        /// <summary>Evaluates occlusion between the listener and a playing source.</summary>
        float Evaluate(in Vector3 listenerPos, in Vector3 sourcePos);
    }
}
