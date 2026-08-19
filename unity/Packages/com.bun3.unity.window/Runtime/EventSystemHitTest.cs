using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Default hit test: raycasts through <see cref="EventSystem.current"/>, so it sees
    /// everything the event system sees — uGUI graphics out of the box, and
    /// sprites/colliders when a <c>Physics2DRaycaster</c>/<c>PhysicsRaycaster</c> sits on
    /// the camera. Results on ignored layers don't count as hits. No EventSystem in the
    /// scene means nothing is ever hit. Zero steady-state allocation.
    /// </summary>
    [Serializable]
    public sealed class EventSystemHitTest : IPointerHitTest
    {
        [SerializeField]
        [Tooltip("Raycast results on these layers do not count as interactive content.")]
        private LayerMask _ignoredLayers = 1 << 2; // built-in "Ignore Raycast"

        private PointerEventData _pointerEventData;
        private readonly List<RaycastResult> _results = new();

        public bool IsHit(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }
            _pointerEventData ??= new PointerEventData(eventSystem);
            _pointerEventData.position = screenPosition;
            _results.Clear();
            eventSystem.RaycastAll(_pointerEventData, _results);
            foreach (var result in _results)
            {
                if ((_ignoredLayers.value & (1 << result.gameObject.layer)) == 0)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
