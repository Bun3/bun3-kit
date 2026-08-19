// TagCatalog partial — build (tag tree construction, ordinal-order layout).
#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags
{
    public sealed partial class TagCatalog
    {
        private static BuildData Build(List<string> canonicalNames)
        {
            canonicalNames.Sort(StringComparer.Ordinal);
            var nodes = new Dictionary<string, BuildNode>(StringComparer.Ordinal);
            foreach (var canonicalName in canonicalNames)
            {
                AddPath(nodes, canonicalName);
            }

            if (nodes.Count > ushort.MaxValue)
            {
                throw new TagCatalogException("Active tag nodes must not exceed 65,535.", string.Empty, 1, 1);
            }

            var root = new BuildNode(string.Empty);
            foreach (var node in nodes.Values)
            {
                var lastDot = node.CanonicalName.LastIndexOf('.');
                if (lastDot < 0)
                {
                    root.Children.Add(node.CanonicalName, node);
                }
                else
                {
                    nodes[node.CanonicalName.Substring(0, lastDot)].Children.Add(node.CanonicalName, node);
                }
            }

            var count = nodes.Count;
            var parents = new ushort[count + 1];
            var subtreeEnds = new ushort[count + 1];
            var byCanonicalName = new Dictionary<string, ushort>(count, StringComparer.Ordinal);
            var nextIndex = 1;
            FillPreorder(root, 0, parents, subtreeEnds, byCanonicalName, ref nextIndex);
            return new BuildData(byCanonicalName, parents, subtreeEnds);
        }

        private readonly struct BuildData
        {
            internal BuildData(
                Dictionary<string, ushort> byCanonicalName,
                ushort[] parents,
                ushort[] subtreeEnds)
            {
                ByCanonicalName = byCanonicalName;
                Parents = parents;
                SubtreeEnds = subtreeEnds;
            }

            internal Dictionary<string, ushort> ByCanonicalName { get; }
            internal ushort[] Parents { get; }
            internal ushort[] SubtreeEnds { get; }
        }

        // Creates nodes for the explicit tag and every ancestor path — parents are implicitly activated.
        private static void AddPath(Dictionary<string, BuildNode> nodes, string canonicalName)
        {
            var start = 0;
            while (true)
            {
                var nextDot = canonicalName.IndexOf('.', start);
                var length = nextDot < 0 ? canonicalName.Length : nextDot;
                var prefix = canonicalName.Substring(0, length);
                if (!nodes.ContainsKey(prefix))
                {
                    nodes.Add(prefix, new BuildNode(prefix));
                }

                if (nextDot < 0) return;
                start = nextDot + 1;
            }
        }

        private static void FillPreorder(
            BuildNode node,
            ushort parent,
            ushort[] parents,
            ushort[] subtreeEnds,
            Dictionary<string, ushort> byCanonicalName,
            ref int nextIndex)
        {
            foreach (var child in node.GetChildrenInOrdinalOrder())
            {
                var index = checked((ushort)nextIndex++);
                parents[index] = parent;
                byCanonicalName.Add(child.CanonicalName, index);
                FillPreorder(child, index, parents, subtreeEnds, byCanonicalName, ref nextIndex);
                subtreeEnds[index] = checked((ushort)(nextIndex - 1));
            }
        }

        private sealed class BuildNode
        {
            internal BuildNode(string canonicalName)
            {
                CanonicalName = canonicalName;
            }

            internal string CanonicalName { get; }
            internal Dictionary<string, BuildNode> Children { get; } = new Dictionary<string, BuildNode>(StringComparer.Ordinal);

            internal List<BuildNode> GetChildrenInOrdinalOrder()
            {
                var children = new List<BuildNode>(Children.Values);
                children.Sort((left, right) => StringComparer.Ordinal.Compare(left.CanonicalName, right.CanonicalName));
                return children;
            }
        }
    }
}
