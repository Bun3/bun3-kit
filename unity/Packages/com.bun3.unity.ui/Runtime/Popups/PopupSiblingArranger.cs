using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 스택 순서가 바뀔 때마다(열림/닫힘/Focus) 팝업들의 sibling index를 스택 순서에 맞춰
    /// 정렬하는 선택 도우미. (순서 통지와 딤 토글은 스택이 직접 하므로 여기서는 트랜스폼만.)
    /// </summary>
    /// <remarks>
    /// 팝업 전용 부모를 전제로 한다 — 부모에 팝업 아닌 자식이 섞여 있으면 인덱스 보장이 없다.
    /// 부모가 서로 다른 팝업이 섞여도 부모별로 상대 순서를 맞춘다.
    /// 스택과 수명을 같이하려면 게임이 <see cref="Dispose"/>를 챙긴다.
    /// </remarks>
    public sealed class PopupSiblingArranger : IDisposable
    {
        private readonly PopupStack _stack;
        private readonly Dictionary<Transform, int> _siblingCounters = new();
        private readonly Action<Popup> _onStackChanged;

        public PopupSiblingArranger(PopupStack stack)
        {
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));

            _onStackChanged = OnStackChanged;
            _stack.Opened += _onStackChanged;
            _stack.Closed += _onStackChanged;
            _stack.Focused += _onStackChanged;
        }

        public void Dispose()
        {
            _stack.Opened -= _onStackChanged;
            _stack.Closed -= _onStackChanged;
            _stack.Focused -= _onStackChanged;
        }

        private void OnStackChanged(Popup popup) => Arrange();

        /// <summary>즉시 재정렬한다. 게임이 팝업 부모를 옮긴 직후 등 수동 갱신용.</summary>
        public void Arrange()
        {
            _siblingCounters.Clear();

            var popups = _stack.Popups;

            for (int i = 0; i < popups.Count; i++)
            {
                var parent = popups[i].transform.parent;
                if (parent == null)
                    continue;

                _siblingCounters.TryGetValue(parent, out var siblingIndex);
                popups[i].transform.SetSiblingIndex(siblingIndex);
                _siblingCounters[parent] = siblingIndex + 1;
            }
        }
    }
}
