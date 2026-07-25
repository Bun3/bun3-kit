using System;
using System.Linq;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
{
    [Serializable]
    public class UnifiedOptionGameObjectActive : UnifiedOption<UnifiedToggleGameObject, GameObject[]>
    {
        protected override GameObject[] GetDefaultOption()
        {
            return Array.Empty<GameObject>();
        }

        protected override void SetOption(UnifiedToggleGameObject component, GameObject[] toActivate)
        {
            var all = _options
                .SelectMany(x => x.option ?? Array.Empty<GameObject>())
                .Where(x => x != null)
                .Distinct();

            var toDeactivate = all.Except(toActivate);
            foreach (var go in toDeactivate)
            {
                if (go != null)
                    go.SetActive(false);
            }

            foreach (var go in toActivate)
            {
                if (go != null)
                    go.SetActive(true);
            }
        }
    }

    [Serializable]
    public partial class UnifiedToggleGameObject : BaseUnifiedToggle<UnifiedToggleGameObject>
    {
        public override UnifiedToggleGameObject Component => this;
    }
}
