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
        internal string SelectedSourceId { get; private set; } = string.Empty;
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
            GameplayTagCatalogFileAdapter.Save(GameSourcePath, RequireEditableSession("game"));
            IsDirty = false;
        }

        internal void DiscardChanges() => IsDirty = false;

        internal void Add(string path, string comment = "")
        {
            var session = RequireEditableSession("game");
            session.Add(path, comment);
            CommitMutation(session);
            SelectedSourceId = "game";
            SelectedPath = GameplayTagCatalogEditSession.Canonicalize(path, nameof(path));
            IsDirty = true;
        }

        internal void SetComment(string path, string comment)
        {
            SetComment("game", path, comment);
        }

        internal void SetComment(string sourceId, string path, string comment)
        {
            var session = RequireEditableSession(sourceId);
            session.SetComment(path, comment);
            CommitMutation(session);
            SelectedSourceId = "game";
            SelectedPath = GameplayTagCatalogEditSession.Canonicalize(path, nameof(path));
            IsDirty = true;
        }

        internal GameplayTagRenameResult RenameSubtree(string path, string newSegment) =>
            RenameSubtree("game", path, newSegment);

        internal GameplayTagRenameResult RenameSubtree(
            string sourceId,
            string path,
            string newSegment)
        {
            var session = RequireEditableSession(sourceId);
            var canonicalPath = GameplayTagCatalogEditSession.Canonicalize(path, nameof(path));
            var result = session.RenameSubtree(path, newSegment);
            SelectedSourceId = "game";
            SelectedPath = result.NewPath;
            if (result.NewPath == canonicalPath)
            {
                return result;
            }

            CommitMutation(session);
            IsDirty = true;
            return result;
        }

        internal int RemoveRedirects(IReadOnlyCollection<string> sources)
        {
            return RemoveRedirects("game", sources);
        }

        internal int RemoveRedirects(string sourceId, IReadOnlyCollection<string> sources)
        {
            var session = RequireEditableSession(sourceId);
            var removed = session.RemoveRedirects(sources);
            if (removed > 0)
            {
                CommitMutation(session);
                IsDirty = true;
            }

            return removed;
        }

        internal void Delete(string path, bool includeDescendants)
        {
            if (includeDescendants)
            {
                throw new InvalidOperationException("Subtree deletion is not supported; delete one explicit tag.");
            }

            DeleteExact("game", path);
        }

        internal void DeleteExact(string path) => DeleteExact("game", path);

        internal void DeleteExact(string sourceId, string path)
        {
            var session = RequireEditableSession(sourceId);
            session.DeleteExact(path);
            CommitMutation(session);
            SelectedSourceId = string.Empty;
            SelectedPath = string.Empty;
            IsDirty = true;
        }

        internal void Select(string path)
        {
            Select("game", path);
        }

        internal void Select(string sourceId, string path)
        {
            if (sourceId is null) throw new ArgumentNullException(nameof(sourceId));
            if (path is null) throw new ArgumentNullException(nameof(path));
            RequireSession();
            SelectedSourceId = sourceId;
            SelectedPath = GameplayTagCatalogEditSession.Canonicalize(path, nameof(path));
        }

        internal bool TryExecute(Action command, out Exception? error)
        {
            if (command is null) throw new ArgumentNullException(nameof(command));

            var workspace = _workspace;
            var session = Session;
            var gameSource = session?.GameSource;
            var selectedSourceId = SelectedSourceId;
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
                if (isDirty && !IsDirty)
                {
                    error = exception;
                    return false;
                }

                _workspace = workspace;
                Session = gameSource is null || session is null
                    ? null
                    : session.Restore(gameSource);
                if (Session is not null)
                {
                    _workspace = workspace.WithGameSession(Session);
                }

                SelectedSourceId = selectedSourceId;
                SelectedPath = selectedPath;
                IsDirty = isDirty;
                error = exception;
                return false;
            }
        }

        private GameplayTagCatalogEditSession RequireEditableSession(string sourceId)
        {
            if (sourceId is null) throw new ArgumentNullException(nameof(sourceId));
            if (!CanEditGameSource)
            {
                throw new InvalidOperationException("The Game Source is not editable while the Workspace is invalid.");
            }

            if (!string.Equals(sourceId, "game", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The selected Tag Source is read-only.");
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
            SelectedSourceId = string.Empty;
            SelectedPath = string.Empty;
            IsDirty = false;
        }

        private void CommitMutation(GameplayTagCatalogEditSession session)
        {
            _workspace = _workspace.WithGameSession(session);
            Session = _workspace.GameSession;
        }
    }
}
