using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bun3.Unity.UI.Popups
{
    /// <summary>
    /// 스택 순서가 바뀔 때마다(열림/닫힘/Focus) 팝업들의 sibling index를 스택 순서에 맞춰
    /// 정렬하고, 각 팝업에 <see cref="PopupBehaviour.OnStackOrderChanged"/>를 통지하는
    /// 선택 도우미. "최상단 팝업만 딤 표시" 같은 표현은 그 훅에서 게임이 처리한다.
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
        private readonly Action<PopupBehaviour> _onStackChanged;

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

        private void OnStackChanged(PopupBehaviour popup) => Arrange();

        /// <summary>즉시 재정렬한다. 게임이 팝업 부모를 옮긴 직후 등 수동 갱신용.</summary>
        public void Arrange()
        {
            _siblingCounters.Clear();

            var popups = _stack.Popups;
            var top = _stack.Top;

            for (int i = 0; i < popups.Count; i++)
            {
                var popup = popups[i];
                var parent = popup.transform.parent;

                if (parent != null)
                {
                    _siblingCounters.TryGetValue(parent, out var siblingIndex);
                    popup.transform.SetSiblingIndex(siblingIndex);
                    _siblingCounters[parent] = siblingIndex + 1;
                }

                popup.OnStackOrderChanged(i, ReferenceEquals(popup, top));
            }
        }
    }
}
