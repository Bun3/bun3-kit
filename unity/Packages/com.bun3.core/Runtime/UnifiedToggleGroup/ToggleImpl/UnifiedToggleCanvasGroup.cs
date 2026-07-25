using System;
using Bun3.Core.Attributes;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
{
    [Serializable]
    public class UnifiedOptionCanvasGroupAlpha : UnifiedOption<CanvasGroup, float>
    {
        protected override float GetDefaultOption() => 1f;

        protected override void SetOption(CanvasGroup component, float value)
        {
            component.alpha = value;
        }
    }

    [Serializable]
    public class UnifiedOptionCanvasGroupInteractable : UnifiedOption<CanvasGroup, bool>
    {
        protected override bool GetDefaultOption() => true;

        protected override void SetOption(CanvasGroup component, bool value)
        {
            component.interactable = value;
        }
    }

    [Serializable]
    public class UnifiedOptionCanvasGroupBlockRaycast : UnifiedOption<CanvasGroup, bool>
    {
        protected override bool GetDefaultOption() => true;

        protected override void SetOption(CanvasGroup component, bool value)
        {
            component.blocksRaycasts = value;
        }
    }

    [RequireComponent(typeof(CanvasGroup))]
    public class UnifiedToggleCanvasGroup : BaseUnifiedToggle<CanvasGroup>
    {
        [SerializeField, ReadOnly]
        protected CanvasGroup _canvasGroup;

        public override CanvasGroup Component => _canvasGroup;

        protected override void EnsureComponent()
        {
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();
        }
    }
}
