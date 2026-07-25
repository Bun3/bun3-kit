using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
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

        // ToggleGroup의 preset 카운트만큼 존재. 사이즈는 SetOptionValues로만 변경.
        [SerializeField] protected List<Option> _options = new();

        public IReadOnlyCollection<Option> Options => _options;

        public void SetOptionValues(string[] values)
        {
            // 더 이상 존재하지 않는 프리셋에 해당하는 옵션 제거
            _options.RemoveAll(opt => !values.Contains(opt.key));

            var currentKeys = _options.Select(opt => opt.key).ToHashSet();

            // 새로 추가된 프리셋에 대한 옵션 추가
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
