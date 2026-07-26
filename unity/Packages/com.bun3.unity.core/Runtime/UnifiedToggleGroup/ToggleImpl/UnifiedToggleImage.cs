using System;
using Bun3.Unity.Core.Attributes;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Scripting.APIUpdating;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionImageSprite : UnifiedOption<Image, Sprite>
    {
        protected override void SetOption(Image component, Sprite value)
        {
            component.sprite = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionImageOverrideSprite : UnifiedOption<Image, Sprite>
    {
        protected override void SetOption(Image component, Sprite value)
        {
            component.overrideSprite = value;
        }
    }

    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
    public class UnifiedOptionImageColor : UnifiedOption<Image, Color>
    {
        protected override Color GetDefaultOption() => Color.white;

        protected override void SetOption(Image component, Color value)
        {
            component.color = value;
        }
    }

    [RequireComponent(typeof(Image))]
    public class UnifiedToggleImage : BaseUnifiedToggle<Image>
    {
        [SerializeField, ReadOnly]
        protected Image _image;

        public override Image Component => _image;

        protected override void EnsureComponent()
        {
            if (_image == null)
                _image = GetComponent<Image>();
        }
    }
}
