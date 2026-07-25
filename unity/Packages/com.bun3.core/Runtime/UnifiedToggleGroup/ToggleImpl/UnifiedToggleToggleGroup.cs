using Bun3.Core.Attributes;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
{
    [System.Serializable]
    public class UnifiedOptionToggleGroup : UnifiedOption<UnifiedToggleGroup, string>
    {
        protected override void SetOption(UnifiedToggleGroup component, string value)
        {
            component.SetValue(value);
        }
    }

    [RequireComponent(typeof(UnifiedToggleGroup))]
    public partial class UnifiedToggleToggleGroup : BaseUnifiedToggle<UnifiedToggleGroup>
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
