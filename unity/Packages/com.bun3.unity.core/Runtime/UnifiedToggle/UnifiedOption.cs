using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [Serializable]
    public abstract class UnifiedOption<TComponent, TOption> : UnifiedOptionBase, IUnifiedOption<TComponent>
    {
        [Serializable]
        public class Option : BaseOption
        {
            public TOption option;
        }

        protected virtual TOption GetDefaultOption()
        {
            return default;
        }

        // One entry per ToggleGroup preset; size changes only via SetOptionValues.
        [SerializeField] protected List<Option> _options = new();

        public IReadOnlyCollection<Option> Options => _options;

        public void SetOptionValues(string[] values)
        {
            // Remove options for presets that no longer exist.
            _options.RemoveAll(opt => !values.Contains(opt.key));

            var currentKeys = _options.Select(opt => opt.key).ToHashSet();

            // Add options for newly added presets.
            foreach (var value in values)
            {
                if (!currentKeys.Contains(value))
                {
                    _options.Add(new Option
                    {
                        key = value,
                        option = GetDefaultOption()
                    });
                }
            }
        }

        public void SetValue(TComponent component, string value)
        {
            if (_options.Count == 0)
                return;

            foreach (var opt in _options)
            {
                if (opt == null || opt.key != value) continue;
                SetOption(component, opt.option);
            }
        }

        protected abstract void SetOption(TComponent component, TOption value);
    }
}
