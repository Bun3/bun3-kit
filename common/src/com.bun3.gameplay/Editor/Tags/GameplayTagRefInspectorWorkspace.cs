#nullable enable
using System;
using Bun3.Gameplay.Tags;
using UnityEditor;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagRefWorkspaceCache
    {
        private readonly Func<GameplayTagEditorWorkspace> _openWorkspace;
        private readonly Func<double> _getTime;
        private readonly double _duration;
        private GameplayTagEditorWorkspace? _current;
        private double _expiresAt;

        internal GameplayTagRefWorkspaceCache(
            Func<GameplayTagEditorWorkspace> openWorkspace,
            Func<double> getTime,
            double duration)
        {
            _openWorkspace = openWorkspace ?? throw new ArgumentNullException(nameof(openWorkspace));
            _getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
            if (duration < 0d) throw new ArgumentOutOfRangeException(nameof(duration));
            _duration = duration;
        }

        internal GameplayTagEditorWorkspace Open()
        {
            var now = _getTime();
            if (_current is not null && now < _expiresAt) return _current;

            _current = _openWorkspace()
                ?? throw new InvalidOperationException("GameplayTag inspector workspace is null.");
            _expiresAt = now + _duration;
            return _current;
        }

        internal void Invalidate()
        {
            _current = null;
            _expiresAt = double.NegativeInfinity;
        }
    }

    [InitializeOnLoad]
    internal static class GameplayTagRefInspectorWorkspace
    {
        private static readonly GameplayTagRefWorkspaceCache Cache = new GameplayTagRefWorkspaceCache(
            OpenUncached,
            () => EditorApplication.timeSinceStartup,
            0.75d);

        static GameplayTagRefInspectorWorkspace()
        {
            EditorApplication.projectChanged -= Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        internal static GameplayTagEditorWorkspace OpenCurrent() => Cache.Open();

        private static GameplayTagEditorWorkspace OpenUncached()
        {
            var gameSourcePath = GameplayTagGameSourcePath.Get(Application.dataPath);
            return GameplayTagEditorWorkspace.Open(
                GameplayTagBuildContextResolver.ResolveDevelopment(gameSourcePath),
                gameSourcePath);
        }

        private static void Invalidate() => Cache.Invalidate();
    }

    internal readonly struct GameplayTagRefFieldState
    {
        private GameplayTagRefFieldState(
            string displayText,
            string tooltip,
            bool hasWarning,
            bool canSelect)
        {
            DisplayText = displayText;
            Tooltip = tooltip;
            HasWarning = hasWarning;
            CanSelect = canSelect;
        }

        internal string DisplayText { get; }
        internal string Tooltip { get; }
        internal bool HasWarning { get; }
        internal bool CanSelect { get; }

        internal static GameplayTagRefFieldState Describe(
            string rawPath,
            bool isMixed,
            GameplayTagEditorWorkspace workspace)
        {
            if (rawPath is null) throw new ArgumentNullException(nameof(rawPath));
            if (workspace is null) throw new ArgumentNullException(nameof(workspace));

            var displayText = isMixed ? "—" : rawPath.Length == 0 ? "None" : rawPath;
            var canSelect = workspace.CanBuildCatalog && workspace.Snapshot is not null;

            if (isMixed)
            {
                return new GameplayTagRefFieldState(
                    displayText,
                    "Selected objects have different GameplayTag values.",
                    false,
                    canSelect);
            }

            if (rawPath.Length == 0)
            {
                return new GameplayTagRefFieldState(
                    "None",
                    "No GameplayTag is referenced.",
                    false,
                    canSelect);
            }

            if (!TagName.TryFold(rawPath, out _))
            {
                return new GameplayTagRefFieldState(
                    displayText,
                    "Serialized GameplayTag path has invalid syntax.",
                    true,
                    canSelect);
            }

            if (!canSelect)
            {
                var tooltip = workspace.Diagnostics.Count == 0
                    ? "Current GameplayTag workspace is invalid."
                    : string.Join(Environment.NewLine, workspace.Diagnostics);
                return new GameplayTagRefFieldState(displayText, tooltip, true, false);
            }

            if (!workspace.Snapshot!.Catalog.TryGet(rawPath, out _))
            {
                return new GameplayTagRefFieldState(
                    displayText,
                    "This GameplayTag is missing from the current runtime catalog.",
                    true,
                    true);
            }

            return new GameplayTagRefFieldState(displayText, rawPath, false, true);
        }
    }
}
