using Bun3.Unity.Core.Attributes;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [System.Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionToggleGroup : UnifiedOption<UnifiedToggleGroup, string>
    {
        protected override void SetOption(UnifiedToggleGroup component, string value)
        {
            component.SetValue(value);
        }
    }

    [RequireComponent(typeof(UnifiedToggleGroup))]
    public class UnifiedToggleToggleGroup : BaseUnifiedToggle<UnifiedToggleGroup>
    {
        [SerializeField, ReadOnly]
        protected UnifiedToggleGroup _group;

        public override UnifiedToggleGroup Component => _group;

        public UnifiedToggleGroup Group => _group;

        protected override void EnsureComponent()
        {
            if (_group == null)
                _group = GetComponent<UnifiedToggleGroup>();
        }
    }
}
