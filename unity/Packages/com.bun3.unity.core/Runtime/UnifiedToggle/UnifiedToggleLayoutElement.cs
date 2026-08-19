using System;
using Bun3.Unity.Core.Attributes;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementIgnoreLayout : UnifiedOption<LayoutElement, bool>
    {
        protected override void SetOption(LayoutElement component, bool value)
        {
            component.ignoreLayout = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementMinWidth : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.minWidth = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementMinHeight : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.minHeight = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementPreferredWidth : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.preferredWidth = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementPreferredHeight : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.preferredHeight = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementFlexibleWidth : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.flexibleWidth = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementFlexibleHeight : UnifiedOption<LayoutElement, float>
    {
        protected override float GetDefaultOption() => -1f;

        protected override void SetOption(LayoutElement component, float value)
        {
            component.flexibleHeight = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionLayoutElementLayoutPriority : UnifiedOption<LayoutElement, int>
    {
        protected override int GetDefaultOption() => 1;

        protected override void SetOption(LayoutElement component, int value)
        {
            component.layoutPriority = value;
        }
    }

    [RequireComponent(typeof(LayoutElement))]
    public class UnifiedToggleLayoutElement : BaseUnifiedToggle<LayoutElement>
    {
        [SerializeField, ReadOnly]
        protected LayoutElement _layoutElement;

        public override LayoutElement Component => _layoutElement;

        protected override void EnsureComponent()
        {
            if (_layoutElement == null)
                _layoutElement = GetComponent<LayoutElement>();
        }
    }
}
