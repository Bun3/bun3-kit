using UnityEngine;

namespace Bun3.Unity.Window
{
    /// <summary>
    /// Answers "is there interactive content at this screen position?" for the
    /// click-through policy (<see cref="ClickThrough.TickPolicy"/>). The position is a
    /// parameter (not read from input internally) so implementations are directly
    /// unit-testable.
    /// </summary>
    public interface IPointerHitTest
    {
        bool IsHit(Vector2 screenPosition);
    }
}
