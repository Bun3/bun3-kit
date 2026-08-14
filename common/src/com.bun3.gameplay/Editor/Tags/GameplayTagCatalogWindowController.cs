#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagCatalogWindowController
    {
        private readonly Func<string, GameplayTagBuildContextResolution> _resolveContext;
        private GameplayTagEditorWorkspace _workspace;

        internal GameplayTagCatalogWindowController()
            : this(
                GameplayTagGameSourcePath.Get(Application.dataPath),
                GameplayTagBuildContextResolver.ResolveDevelopment)
        {
        }

        internal GameplayTagCatalogWindowController(
            string gameSourcePath,
            Func<string, GameplayTagBuildContextResolution> resolveContext)
        {
            if (gameSourcePath is null) throw new ArgumentNullException(nameof(gameSourcePath));
            _resolveContext = resolveContext ?? throw new ArgumentNullException(nameof(resolveContext));
            GameSourcePath = Path.GetFullPath(gameSourcePath);
            _workspace = OpenWorkspace();
        }

        internal string GameSourcePath { get; }
        internal GameplayTagEditorWorkspace Workspace => _workspace;
        internal GameplayTagCatalogEditSession? Session { get; private set; }
        internal string SelectedPath { get; private set; } = string.Empty;
        internal bool IsDirty { get; private set; }
        internal bool CanCreateGameSource => _workspace.CanCreateGameSource;
        internal bool CanEditGameSource => _workspace.CanEditGameSource;
        internal bool CanBuildCatalog => _workspace.CanBuildCatalog;

        internal void CreateGameSource()
        {
            if (!CanCreateGameSource)
            {
                throw new InvalidOperationException("The fixed Game Source cannot be created in the current state.");
            }

            GameplayTagCatalogFileAdapter.CreateGameSource(GameSourcePath);
            ReplaceWorkspace();
        }

        internal void ImportExisting(string sourcePath)
        {
            if (sourcePath is null) throw new ArgumentNullException(nameof(sourcePath));
            var candidate = GameplayTagCatalogFileAdapter.PrepareImport(
                sourcePath,
                GameSourcePath);
            var candidateWorkspace = GameplayTagEditorWorkspace.Open(
                _resolveContext(sourcePath),
                candidate);
            if (!candidateWorkspace.CanEditGameSource)
            {
                throw new InvalidOperationException(
                    "The imported Game Source is invalid in the resolved Workspace: "
                    + string.Join(Environment.NewLine, candidateWorkspace.Diagnostics));
            }

            GameplayTagCatalogFileAdapter.ImportExisting(candidate, GameSourcePath);
            ReplaceWorkspace(candidateWorkspace);
        }

        internal bool Reload(bool discardDirty)
        {
            if (IsDirty && !discardDirty) return false;
            ReplaceWorkspace();
            return true;
        }

        internal void Save()
        {
            GameplayTagCatalogFileAdapter.Save(GameSourcePath, RequireEditableSession());
            IsDirty = false;
        }

        internal void DiscardChanges() => IsDirty = false;

        internal void Add(string path, string comment = "")
        {
            RequireEditableSession().Add(path, comment);
            SelectedPath = path;
            IsDirty = true;
        }

        internal void SetComment(string path, string comment)
        {
            RequireEditableSession().SetComment(path, comment);
            SelectedPath = path;
            IsDirty = true;
        }

        internal void RenameSubtree(string path, string newSegment)
        {
            SelectedPath = RequireEditableSession().RenameSubtree(path, newSegment);
            IsDirty = true;
        }

        internal int RemoveRedirects(IReadOnlyCollection<string> sources)
        {
            var removed = RequireEditableSession().RemoveRedirects(sources);
            if (removed > 0) IsDirty = true;
            return removed;
        }

        internal void Delete(string path, bool includeDescendants)
        {
            RequireEditableSession().Delete(path, includeDescendants);
            SelectedPath = string.Empty;
            IsDirty = true;
        }

        internal void Select(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            RequireSession();
            SelectedPath = path;
        }

        internal bool TryExecute(Action command, out Exception? error)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));

            var workspace = _workspace;
            var serializedSession = Session?.Serialize();
            var selectedPath = SelectedPath;
            var isDirty = IsDirty;
            try
            {
                command();
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                _workspace = workspace;
                Session = serializedSession is null
                    ? null
                    : GameplayTagCatalogEditSession.Open(serializedSession);
                SelectedPath = selectedPath;
                IsDirty = isDirty;
                error = exception;
                return false;
            }
        }

        private GameplayTagCatalogEditSession RequireEditableSession()
        {
            if (!CanEditGameSource)
            {
                throw new InvalidOperationException("The Game Source is not editable while the Workspace is invalid.");
            }

            return RequireSession();
        }

        private GameplayTagCatalogEditSession RequireSession()
        {
            return Session ?? throw new InvalidOperationException("No gameplay tag catalog is open.");
        }

        private GameplayTagEditorWorkspace OpenWorkspace()
        {
            var workspace = GameplayTagEditorWorkspace.Open(
                _resolveContext(GameSourcePath),
                GameSourcePath);
            Session = workspace.GameSession;
            return workspace;
        }

        private void ReplaceWorkspace()
        {
            ReplaceWorkspace(OpenWorkspace());
        }

        private void ReplaceWorkspace(GameplayTagEditorWorkspace workspace)
        {
            _workspace = workspace;
            Session = workspace.GameSession;
            SelectedPath = string.Empty;
            IsDirty = false;
        }
    }
}
