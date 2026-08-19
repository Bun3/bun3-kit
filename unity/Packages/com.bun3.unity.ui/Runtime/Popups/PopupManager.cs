using System;
using Bun3.Unity.Core.Utils;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// Assembled popup pieces (<see cref="PopupStack"/> + optional pool / back-key router /
    /// sibling arranger). Built via <see cref="PopupManagerBuilder"/>; <see cref="Dispose"/>
    /// guarantees teardown order.
    /// </summary>
    /// <remarks>Partial layout: this file (assembly/lifetime/global slot) / Facade (stack delegation).</remarks>
    public sealed partial class PopupManager : IDisposable
    {
        /// <summary>
        /// Optional global access slot. Assign in game bootstrap
        /// (<c>PopupManager.Instance = new PopupManagerBuilder(...).Build();</c>) and use
        /// <c>PopupManager.Instance.Push(...)</c> anywhere. <see cref="PopupManagerBuilder.Build"/>
        /// does not auto-assign so per-scene/test managers stay possible — going global is the
        /// game's choice. The slot clears automatically when the assigned instance is
        /// <see cref="Dispose"/>d.
        /// </summary>
        public static PopupManager Instance { get; set; }

        // With domain reload disabled (Enter Play Mode Options), prevents a previous play
        // session's instance (possibly pointing at destroyed objects) from surviving into the next.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        /// <summary>Popup stack. Always present.</summary>
        public PopupStack Stack { get; }

        /// <summary>Instance pool. Only with <see cref="PopupManagerBuilder.UsePool"/>; otherwise null.</summary>
        public PopupPool Pool { get; }

        /// <summary>Back-key router. Only with <see cref="PopupManagerBuilder.UseBackKey"/>; otherwise null.</summary>
        public PopupBackKeyRouter BackKeyRouter { get; }

        /// <summary>Sibling arranger. Only with <see cref="PopupManagerBuilder.UseSiblingArranger"/>; otherwise null.</summary>
        public PopupSiblingArranger Arranger { get; }

        internal PopupManager(PopupStack stack, PopupPool pool,
            PopupBackKeyRouter backKeyRouter, PopupSiblingArranger arranger)
        {
            Stack = stack;
            Pool = pool;
            BackKeyRouter = backKeyRouter;
            Arranger = arranger;
        }

        /// <summary>
        /// Releases everything in order: remove router → detach arranger → clear stack → destroy pool.
        /// Also clears the slot if this instance was <see cref="Instance"/>.
        /// </summary>
        public void Dispose()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;

            BackKeyRouter.SafeDestroy();

            Arranger?.Dispose();
            Stack.Dispose();
            Pool?.Dispose();
        }
    }
}
