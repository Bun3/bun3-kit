using System.Collections.Generic;
using Bun3.Unity.Core.PlayerLoop;
using UnityEngine;
using UnityEngine.UI;

namespace Bun3.Unity.Core.Motion
{
    /// <summary>
    /// Short feedback motions — punch, shake and flash — for UI and world objects.
    /// Each motion remembers the target's starting value and restores it exactly when it ends.
    /// Triggering the same kind on the same target again restarts from that remembered value,
    /// so repeated triggers never accumulate. Motions run on unscaled time from a player-loop
    /// system after <c>LateUpdate</c>, without a scene object, and only in play mode.
    /// </summary>
    /// <remarks>
    /// Do not target a property that another script writes every frame; the two writes overwrite each other.
    /// </remarks>
    public static class Juice
    {
        private enum Kind : byte
        {
            Punch,
            Shake,
            Flash,
        }

        private struct Entry
        {
            public Kind Kind;
            public Object Target;
            public Vector3 Base;
            public Color BaseColor;
            public Color FlashColor;
            public float Amount;
            public float Seconds;
            public float Elapsed;
        }

        private static class TickMarker
        {
        }

        private static readonly List<Entry> Entries = new();

        /// <summary>Scales <paramref name="target"/> up to <paramref name="scale"/> times its size and back.</summary>
        public static void Punch(Transform target, float scale = 1.15f, float seconds = 0.12f)
        {
            if (target == null)
                return;

            Start(new Entry { Kind = Kind.Punch, Target = target, Base = target.localScale, Amount = scale, Seconds = seconds });
        }

        /// <summary>
        /// Shakes <paramref name="target"/> sideways, shrinking from <paramref name="distance"/> to rest.
        /// The distance is in local units: pixels for UI, world units for world objects.
        /// </summary>
        public static void Shake(Transform target, float distance = 6f, float seconds = 0.18f)
        {
            if (target == null)
                return;

            Start(new Entry { Kind = Kind.Shake, Target = target, Base = target.localPosition, Amount = distance, Seconds = seconds });
        }

        /// <summary>Tints <paramref name="target"/> with <paramref name="color"/> and fades back to its own color.</summary>
        public static void Flash(Graphic target, Color color, float seconds = 0.15f)
        {
            if (target == null)
                return;

            Start(new Entry { Kind = Kind.Flash, Target = target, BaseColor = target.color, FlashColor = color, Seconds = seconds });
        }

        /// <summary>Tints <paramref name="target"/> with <paramref name="color"/> and fades back to its own color.</summary>
        public static void Flash(SpriteRenderer target, Color color, float seconds = 0.15f)
        {
            if (target == null)
                return;

            Start(new Entry { Kind = Kind.Flash, Target = target, BaseColor = target.color, FlashColor = color, Seconds = seconds });
        }

        private static void Start(Entry entry)
        {
            if (entry.Seconds <= 0f || !Application.isPlaying)
                return;

            for (var i = 0; i < Entries.Count; i++)
            {
                var running = Entries[i];
                if (running.Kind != entry.Kind || running.Target != entry.Target)
                    continue;

                entry.Base = running.Base;
                entry.BaseColor = running.BaseColor;
                Entries[i] = entry;
                return;
            }

            Entries.Add(entry);
            if (!PlayerLoopSystemHelper.IsInserted(typeof(TickMarker)))
            {
                PlayerLoopSystemHelper.InsertSystemAfter(typeof(TickMarker), Tick,
                    typeof(UnityEngine.PlayerLoop.PreLateUpdate.ScriptRunBehaviourLateUpdate));
            }
        }

        private static void Tick()
        {
            var deltaSeconds = Time.unscaledDeltaTime;
            for (var i = Entries.Count - 1; i >= 0; i--)
            {
                var entry = Entries[i];
                if (entry.Target == null)
                {
                    Entries.RemoveAt(i);
                    continue;
                }

                entry.Elapsed += deltaSeconds;
                var t = Mathf.Clamp01(entry.Elapsed / entry.Seconds);
                Apply(entry, t);
                if (t >= 1f)
                    Entries.RemoveAt(i);
                else
                    Entries[i] = entry;
            }
        }

        private static void Apply(in Entry entry, float t)
        {
            switch (entry.Kind)
            {
                case Kind.Punch:
                    ((Transform)entry.Target).localScale = t >= 1f
                        ? entry.Base
                        : entry.Base * (1f + (entry.Amount - 1f) * Mathf.Sin(t * Mathf.PI));
                    break;
                case Kind.Shake:
                    // Three swings that shrink linearly to rest.
                    var offset = t >= 1f ? 0f : Mathf.Sin(t * Mathf.PI * 6f) * entry.Amount * (1f - t);
                    ((Transform)entry.Target).localPosition = entry.Base + new Vector3(offset, 0f, 0f);
                    break;
                case Kind.Flash:
                    var color = t >= 1f ? entry.BaseColor : Color.Lerp(entry.FlashColor, entry.BaseColor, t);
                    if (entry.Target is Graphic graphic)
                        graphic.color = color;
                    else
                        ((SpriteRenderer)entry.Target).color = color;
                    break;
            }
        }

        // Play mode may start without a domain reload; the player-loop helper drops the tick on exit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Entries.Clear();
    }
}
