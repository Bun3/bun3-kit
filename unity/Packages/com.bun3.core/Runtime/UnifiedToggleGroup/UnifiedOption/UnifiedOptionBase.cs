using System;
using Bun3.Core.Attributes;
using UnityEngine;

namespace Bun3.Core.UnifiedToggle
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
