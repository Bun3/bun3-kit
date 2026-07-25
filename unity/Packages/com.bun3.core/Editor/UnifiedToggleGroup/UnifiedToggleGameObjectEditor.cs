using Bun3.Core.UnifiedToggle;
using UnityEditor;

namespace Bun3.Core.Editor.UnifiedToggle
{
    [CustomEditor(typeof(UnifiedToggleGameObject), true)]
    public class UnifiedToggleGameObjectEditor : BaseUnifiedToggleEditor
    {
        protected override void OnOptionChanged()
        {
            base.OnOptionChanged();
        }
    }
}
