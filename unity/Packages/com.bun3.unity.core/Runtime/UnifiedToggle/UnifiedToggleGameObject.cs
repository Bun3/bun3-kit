using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [Serializable]
    [MovedFrom(true, sourceNamespace: "Bun3.Core.UnifiedToggle", sourceAssembly: "bun3.core")]
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
    public class UnifiedToggleGameObject : BaseUnifiedToggle<UnifiedToggleGameObject>
    {
        public override UnifiedToggleGameObject Component => this;
    }
}
