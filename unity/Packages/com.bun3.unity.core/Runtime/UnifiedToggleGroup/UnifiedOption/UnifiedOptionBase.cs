using System;
using Bun3.Unity.Core.Attributes;
using UnityEngine;

namespace Bun3.Unity.Core.UnifiedToggle
{
    [Serializable]
    public abstract class UnifiedOptionBase
    {
        [Serializable]
        public class BaseOption
        {
            [HideInInspector] public string key;
        }
    }
}
