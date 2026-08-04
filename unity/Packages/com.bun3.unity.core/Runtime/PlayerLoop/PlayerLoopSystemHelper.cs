using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Bun3.Unity.Core.PlayerLoop
{
    /// <summary>
    /// Inserts static per-frame callbacks into Unity's player loop — ticking for static
    /// systems without a scene object. Each callback is registered under a marker type
    /// relative to a built-in phase (e.g.
    /// <c>UnityEngine.PlayerLoop.Update.ScriptRunBehaviourUpdate</c>).
    /// Inserted systems are removed automatically on <see cref="Application.quitting"/>
    /// (which the editor raises on play-mode exit, so systems never leak into edit mode).
    /// Adapted from Baste Rainbow's PlayerLoopInterface.
    /// </summary>
    public static class PlayerLoopSystemHelper
    {
        private static readonly List<PlayerLoopSystem> InsertedSystems = new();

        private enum InsertType
        {
            Before,
            After,
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [RuntimeInitializeOnLoadMethod]
#endif
        private static void Initialize()
        {
            Application.quitting -= ClearInsertedSystems;
            Application.quitting += ClearInsertedSystems;
        }

        private static void ClearInsertedSystems()
        {
            // TryRemoveSystem removes the matching entry from InsertedSystems, so walk
            // backwards by index instead of enumerating.
            for (var i = InsertedSystems.Count - 1; i >= 0; i--)
            {
                TryRemoveSystem(InsertedSystems[i].type);
            }
            InsertedSystems.Clear();
        }

        /// <summary>Inserts a callback that runs every frame just before <paramref name="insertBefore"/>.</summary>
        public static void InsertSystemBefore(Type newSystemMarker, PlayerLoopSystem.UpdateFunction newSystemUpdate, Type insertBefore)
        {
            InsertSystem(
                new PlayerLoopSystem { type = newSystemMarker, updateDelegate = newSystemUpdate },
                insertBefore, InsertType.Before);
        }

        /// <summary>Inserts a callback that runs every frame just after <paramref name="insertAfter"/>.</summary>
        public static void InsertSystemAfter(Type newSystemMarker, PlayerLoopSystem.UpdateFunction newSystemUpdate, Type insertAfter)
        {
            InsertSystem(
                new PlayerLoopSystem { type = newSystemMarker, updateDelegate = newSystemUpdate },
                insertAfter, InsertType.After);
        }

        /// <summary>True when a system with this marker type is currently registered by this helper.</summary>
        public static bool IsInserted(Type systemMarker)
        {
            foreach (var system in InsertedSystems)
            {
                if (system.type == systemMarker)
                {
                    return true;
                }
            }
            return false;
        }

        private static void InsertSystem(PlayerLoopSystem toInsert, Type insertTarget, InsertType insertType)
        {
            if (toInsert.type == null)
            {
                throw new ArgumentException("The inserted player loop system must have a marker type!", nameof(toInsert));
            }
            if (toInsert.updateDelegate == null)
            {
                throw new ArgumentException("The inserted player loop system must have an update delegate!", nameof(toInsert));
            }
            if (insertTarget == null)
            {
                throw new ArgumentNullException(nameof(insertTarget));
            }

            var rootSystem = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            InsertSystem(ref rootSystem, toInsert, insertTarget, insertType, out var couldInsert);
            if (!couldInsert)
            {
                throw new ArgumentException(
                    $"When trying to insert the type {toInsert.type.Name} into the player loop {insertType.ToString().ToLowerInvariant()} {insertTarget.Name}, {insertTarget.Name} could not be found in the current player loop!");
            }

            InsertedSystems.Add(toInsert);
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(rootSystem);
        }

        /// <summary>
        /// Removes the first system whose marker type matches. Returns whether one was found.
        /// </summary>
        public static bool TryRemoveSystem(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type), "Trying to remove a null type!");
            }

            var currentSystem = UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop();
            var couldRemove = TryRemoveTypeFrom(ref currentSystem, type);
            UnityEngine.LowLevel.PlayerLoop.SetPlayerLoop(currentSystem);

            if (couldRemove)
            {
                for (var i = InsertedSystems.Count - 1; i >= 0; i--)
                {
                    if (InsertedSystems[i].type == type)
                    {
                        InsertedSystems.RemoveAt(i);
                        break;
                    }
                }
            }
            return couldRemove;
        }

        private static bool TryRemoveTypeFrom(ref PlayerLoopSystem currentSystem, Type type)
        {
            var subSystems = currentSystem.subSystemList;
            if (subSystems == null)
            {
                return false;
            }

            for (var i = 0; i < subSystems.Length; i++)
            {
                if (subSystems[i].type == type)
                {
                    var newSubSystems = new PlayerLoopSystem[subSystems.Length - 1];
                    Array.Copy(subSystems, newSubSystems, i);
                    Array.Copy(subSystems, i + 1, newSubSystems, i, subSystems.Length - i - 1);
                    currentSystem.subSystemList = newSubSystems;
                    return true;
                }

                if (TryRemoveTypeFrom(ref subSystems[i], type))
                {
                    return true;
                }
            }
            return false;
        }

        private static void InsertSystem(
            ref PlayerLoopSystem currentLoopRecursive,
            PlayerLoopSystem toInsert,
            Type insertTarget,
            InsertType insertType,
            out bool couldInsert)
        {
            var currentSubSystems = currentLoopRecursive.subSystemList;
            if (currentSubSystems == null)
            {
                couldInsert = false;
                return;
            }

            var indexOfTarget = -1;
            for (var i = 0; i < currentSubSystems.Length; i++)
            {
                if (currentSubSystems[i].type == insertTarget)
                {
                    indexOfTarget = i;
                    break;
                }
            }

            if (indexOfTarget != -1)
            {
                var newSubSystems = new PlayerLoopSystem[currentSubSystems.Length + 1];
                var insertIndex = insertType == InsertType.Before ? indexOfTarget : indexOfTarget + 1;

                for (var i = 0; i < newSubSystems.Length; i++)
                {
                    if (i < insertIndex)
                    {
                        newSubSystems[i] = currentSubSystems[i];
                    }
                    else if (i == insertIndex)
                    {
                        newSubSystems[i] = toInsert;
                    }
                    else
                    {
                        newSubSystems[i] = currentSubSystems[i - 1];
                    }
                }

                couldInsert = true;
                currentLoopRecursive.subSystemList = newSubSystems;
            }
            else
            {
                for (var i = 0; i < currentSubSystems.Length; i++)
                {
                    var subSystem = currentSubSystems[i];
                    InsertSystem(ref subSystem, toInsert, insertTarget, insertType, out var couldInsertInInner);
                    if (couldInsertInInner)
                    {
                        currentSubSystems[i] = subSystem;
                        couldInsert = true;
                        return;
                    }
                }
                couldInsert = false;
            }
        }

        /// <summary>String dump of the current player loop, for debugging.</summary>
        public static string CurrentLoopToString()
        {
            var sb = new StringBuilder();
            AppendRecursively(UnityEngine.LowLevel.PlayerLoop.GetCurrentPlayerLoop(), 0);
            return sb.ToString();

            void AppendRecursively(PlayerLoopSystem system, int depth)
            {
                sb.Append(' ', depth * 2);
                // The root system has a null type; all others have a marker type.
                sb.AppendLine(system.type?.Name ?? "ROOT");
                if (system.subSystemList == null)
                {
                    return;
                }
                foreach (var subSystem in system.subSystemList)
                {
                    AppendRecursively(subSystem, depth + 1);
                }
            }
        }
    }
}
