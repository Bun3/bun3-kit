using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Bun3.Unity.Colliders.Editor
{
    /// <summary>
    /// Scene view tool for the active collider: click a point to select it, Shift or Ctrl/Cmd click to toggle, drag a box
    /// to select (Shift or Ctrl/Cmd adds), Edit &gt; Delete to delete the selection, Esc to clear it.
    /// </summary>
    public abstract class ColliderPointTool : EditorTool
    {
        const float PickRadius = 8, DragThreshold = 3;
        static readonly Color PointColor = new Color(1, 1, 1, .9f), SelectedColor = new Color(1, .7f, .1f, 1);

        readonly HashSet<ColliderPoint> selected = new HashSet<ColliderPoint>();
        Collider2D owner;
        Vector2 dragStart;
        bool dragging;
        GUIStyle selectionRect;

        /// <inheritdoc/>
        public override void OnActivated() => Undo.undoRedoPerformed += ClearSelection;

        /// <inheritdoc/>
        public override void OnWillBeDeactivated()
        {
            Undo.undoRedoPerformed -= ClearSelection;
            ClearSelection();
        }

        /// <inheritdoc/>
        public override void OnToolGUI(EditorWindow window)
        {
            if (!(window is SceneView) || !(target is Collider2D collider)) return;
            if (collider != owner)
            {
                selected.Clear();
                owner = collider;
            }
            var e = Event.current;
            int control = GUIUtility.GetControlID(FocusType.Passive);
            switch (e.GetTypeForControl(control))
            {
                case EventType.Layout:
                    HandleUtility.AddDefaultControl(control);
                    break;
                case EventType.MouseDown when e.button == 0 && !e.alt:
                    GUIUtility.hotControl = control;
                    dragStart = e.mousePosition;
                    dragging = false;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == control:
                    dragging |= (e.mousePosition - dragStart).magnitude > DragThreshold;
                    window.Repaint();
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    bool add = e.shift || EditorGUI.actionKey;
                    if (dragging) SelectInRect(collider, DragRect(e.mousePosition), add);
                    else Click(collider, e.mousePosition, add);
                    dragging = false;
                    e.Use();
                    break;
                case EventType.KeyDown when e.keyCode == KeyCode.Escape && selected.Count > 0:
                    selected.Clear();
                    e.Use();
                    break;
                // Only with points selected, so Edit > Delete still deletes the GameObject otherwise.
                case EventType.ValidateCommand when IsDelete(e.commandName) && selected.Count > 0:
                    e.Use();
                    break;
                case EventType.ExecuteCommand when IsDelete(e.commandName) && selected.Count > 0:
                    if (!ColliderPoints.Delete(collider, selected))
                        Debug.LogWarning("Deleting these points would leave the collider without a shape, so nothing was deleted.", collider);
                    selected.Clear();
                    e.Use();
                    break;
                case EventType.Repaint:
                    Draw(collider, e.mousePosition, GUIUtility.hotControl == control && dragging);
                    break;
            }
        }

        static bool IsDelete(string command) => command == "SoftDelete" || command == "Delete";

        void ClearSelection()
        {
            selected.Clear();
            SceneView.RepaintAll();
        }

        void Click(Collider2D collider, Vector2 mouse, bool add)
        {
            if (!add) selected.Clear();
            float best = PickRadius;
            ColliderPoint? hit = null;
            ForEachPoint(collider, (point, gui) =>
            {
                float distance = Vector2.Distance(gui, mouse);
                if (distance > best) return;
                best = distance;
                hit = point;
            });
            if (hit is ColliderPoint picked && !selected.Remove(picked)) selected.Add(picked);
        }

        void SelectInRect(Collider2D collider, Rect rect, bool add)
        {
            if (!add) selected.Clear();
            ForEachPoint(collider, (point, gui) =>
            {
                if (rect.Contains(gui)) selected.Add(point);
            });
        }

        void Draw(Collider2D collider, Vector2 mouse, bool showRect)
        {
            for (int p = 0; p < ColliderPoints.PathCount(collider); p++)
            {
                var path = ColliderPoints.GetPath(collider, p);
                for (int i = 0; i < path.Length; i++)
                {
                    var world = ColliderPoints.ToWorld(collider, path[i]);
                    Handles.color = selected.Contains(new ColliderPoint(p, i)) ? SelectedColor : PointColor;
                    Handles.DotHandleCap(0, world, Quaternion.identity, HandleUtility.GetHandleSize(world) * .04f, EventType.Repaint);
                }
            }
            if (!showRect) return;
            Handles.BeginGUI();
            (selectionRect ??= "SelectionRect").Draw(DragRect(mouse), false, false, false, false);
            Handles.EndGUI();
        }

        static void ForEachPoint(Collider2D collider, System.Action<ColliderPoint, Vector2> visit)
        {
            for (int p = 0; p < ColliderPoints.PathCount(collider); p++)
            {
                var path = ColliderPoints.GetPath(collider, p);
                for (int i = 0; i < path.Length; i++)
                    visit(new ColliderPoint(p, i), HandleUtility.WorldToGUIPoint(ColliderPoints.ToWorld(collider, path[i])));
            }
        }

        Rect DragRect(Vector2 mouse) => Rect.MinMaxRect(Mathf.Min(dragStart.x, mouse.x), Mathf.Min(dragStart.y, mouse.y),
            Mathf.Max(dragStart.x, mouse.x), Mathf.Max(dragStart.y, mouse.y));
    }

    /// <summary><see cref="ColliderPointTool"/> for <see cref="PolygonCollider2D"/>.</summary>
    [EditorTool("Select Polygon Collider Points", typeof(PolygonCollider2D))]
    public sealed class PolygonColliderPointTool : ColliderPointTool
    {
    }

    /// <summary><see cref="ColliderPointTool"/> for <see cref="EdgeCollider2D"/>.</summary>
    [EditorTool("Select Edge Collider Points", typeof(EdgeCollider2D))]
    public sealed class EdgeColliderPointTool : ColliderPointTool
    {
    }
}
