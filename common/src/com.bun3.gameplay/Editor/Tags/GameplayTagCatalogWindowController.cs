#nullable enable
using System;

namespace Bun3.Gameplay.Editor.Tags
{
    internal sealed class GameplayTagCatalogWindowController
    {
        internal string FilePath { get; private set; } = string.Empty;
        internal GameplayTagCatalogEditSession? Session { get; private set; }
        internal string SelectedPath { get; private set; } = string.Empty;
        internal bool IsDirty { get; private set; }

        internal void New(string absolutePath)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));

            var session = GameplayTagCatalogEditSession.Open("{\"schemaVersion\":1,\"tags\":[]}");
            GameplayTagCatalogFileAdapter.Save(absolutePath, session);
            FilePath = absolutePath;
            Session = session;
            SelectedPath = string.Empty;
            IsDirty = false;
        }

        internal void Open(string absolutePath)
        {
            if (absolutePath is null) throw new ArgumentNullException(nameof(absolutePath));

            var session = GameplayTagCatalogFileAdapter.Load(absolutePath);
            FilePath = absolutePath;
            Session = session;
            SelectedPath = string.Empty;
            IsDirty = false;
        }

        internal bool Reload(bool discardDirty)
        {
            if (IsDirty && !discardDirty) return false;
            var filePath = RequireFilePath();
            var session = GameplayTagCatalogFileAdapter.Load(filePath);
            Session = session;
            SelectedPath = string.Empty;
            IsDirty = false;
            return true;
        }

        internal void Save()
        {
            GameplayTagCatalogFileAdapter.Save(RequireFilePath(), RequireSession());
            IsDirty = false;
        }

        internal void Add(string path, string comment = "")
        {
            RequireSession().Add(path, comment);
            SelectedPath = path;
            IsDirty = true;
        }

        internal void SetComment(string path, string comment)
        {
            RequireSession().SetComment(path, comment);
            SelectedPath = path;
            IsDirty = true;
        }

        internal void RelocateSubtree(string oldPath, string newPath)
        {
            RequireSession().RelocateSubtree(oldPath, newPath);
            SelectedPath = newPath;
            IsDirty = true;
        }

        internal void Delete(string path, bool includeDescendants)
        {
            RequireSession().Delete(path, includeDescendants);
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

            var filePath = FilePath;
            var session = Session;
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
                FilePath = filePath;
                Session = session;
                SelectedPath = selectedPath;
                IsDirty = isDirty;
                error = exception;
                return false;
            }
        }

        private string RequireFilePath()
        {
            if (FilePath.Length == 0)
            {
                throw new InvalidOperationException("No gameplay tag catalog is open.");
            }

            return FilePath;
        }

        private GameplayTagCatalogEditSession RequireSession()
        {
            return Session ?? throw new InvalidOperationException("No gameplay tag catalog is open.");
        }
    }
}
