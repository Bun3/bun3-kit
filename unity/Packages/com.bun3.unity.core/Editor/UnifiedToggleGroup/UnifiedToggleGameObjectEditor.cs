using Bun3.Unity.Core.UnifiedToggle;
using UnityEditor;

namespace Bun3.Unity.Core.Editor.UnifiedToggle
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
