#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagRenameResult
    {
        private readonly IReadOnlyList<string> _shadowedOldPaths;

        internal GameplayTagRenameResult(string newPath, IReadOnlyList<string> shadowedOldPaths)
        {
            NewPath = newPath ?? throw new ArgumentNullException(nameof(newPath));
            if (shadowedOldPaths is null) throw new ArgumentNullException(nameof(shadowedOldPaths));
            var copy = new string[shadowedOldPaths.Count];
            for (var index = 0; index < copy.Length; index++)
            {
                copy[index] = shadowedOldPaths[index]
                    ?? throw new ArgumentNullException(nameof(shadowedOldPaths));
            }

            _shadowedOldPaths = Array.AsReadOnly(copy);
        }

        internal string NewPath { get; }

        internal IReadOnlyList<string> ShadowedOldPaths => _shadowedOldPaths;
    }
}
