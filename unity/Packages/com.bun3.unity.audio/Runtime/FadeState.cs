using UnityEngine;

namespace Bun3.Unity.Audio
{
    /// <summary>
    /// Coroutine-free fade progression driven by injected delta time.
    /// Callers own terminal-state decisions; Advance only reports completion.
    /// </summary>
    internal struct FadeState
    {
        public float Elapsed;
        public float Duration;
        public float From;
        public float To;
        public float Factor;

        /// <summary>Starts a fade toward <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
        public void Begin(float from, float to, float duration)
        {
            From = from;
            To = to;
            Factor = from;
            Elapsed = 0f;
            Duration = duration;
        }

        /// <summary>Stops any running fade and pins the factor.</summary>
        public void SetInstant(float factor)
        {
            Duration = 0f;
            Factor = factor;
        }

        /// <summary>Advances a running fade; returns true on the tick the fade finishes.</summary>
        public bool Advance(float dt)
        {
            if (Duration <= 0f)
            {
                return false;
            }
            Elapsed += dt;
            var t = Mathf.Clamp01(Elapsed / Duration);
            Factor = Mathf.Lerp(From, To, t);
            if (t < 1f)
            {
                return false;
            }
            Duration = 0f;
            return true;
        }
    }
}
