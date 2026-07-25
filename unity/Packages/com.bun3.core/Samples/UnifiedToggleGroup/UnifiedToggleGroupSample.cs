using Bun3.Core.UnifiedToggle;
using UnityEngine;

namespace Bun3.Core.Samples
{
    /// <summary>
    /// Demonstrates programmatic control of <see cref="UnifiedToggleGroup"/>.
    /// Set up a group with several toggles in the inspector, then drive it from
    /// code by passing one of its preset names to <see cref="UnifiedToggleGroup.SetValue"/>.
    /// </summary>
    public class UnifiedToggleGroupSample : MonoBehaviour
    {
        [SerializeField] private UnifiedToggleGroup _group;

        [Tooltip("Preset name to apply when this component starts.")]
        [SerializeField] private string _initialPreset = "On";

        private void Start()
        {
            if (_group == null)
                return;

            _group.SetValue(_initialPreset);
        }

        public void Toggle()
        {
            if (_group == null)
                return;

            _group.SetOn(!_group.isOn);
        }

        public void Apply(string preset)
        {
            if (_group == null)
                return;

            _group.SetValue(preset);
        }
    }
}
