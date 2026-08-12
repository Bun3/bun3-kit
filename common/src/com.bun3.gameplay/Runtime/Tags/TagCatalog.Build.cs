#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Tags
{
    public sealed partial class TagCatalog
    {
        private static TagCatalog Build(List<ExplicitTag> explicitTags)
        {
            explicitTags.Sort((left, right) => StringComparer.Ordinal.Compare(left.CanonicalName, right.CanonicalName));
            var nodes = new Dictionary<string, BuildNode>(StringComparer.Ordinal);
            foreach (var explicitTag in explicitTags)
            {
                AddPath(nodes, explicitTag);
            }

            if (nodes.Count > ushort.MaxValue)
            {
                throw new TagCatalogException("활성 태그 노드는 65,535개를 넘을 수 없습니다.", string.Empty, 1, 1);
            }

            var root = new BuildNode(string.Empty, string.Empty);
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
            var displayNames = new string[count + 1];
            var parents = new ushort[count + 1];
            var subtreeEnds = new ushort[count + 1];
            var byCanonicalName = new Dictionary<string, ushort>(count, StringComparer.Ordinal);
            var nextIndex = 1;
            FillPreorder(root, 0, displayNames, parents, subtreeEnds, byCanonicalName, ref nextIndex);
            return new TagCatalog(byCanonicalName, displayNames, parents, subtreeEnds);
        }

        private static void AddPath(Dictionary<string, BuildNode> nodes, ExplicitTag explicitTag)
        {
            var canonicalEnd = 0;
            var displayEnd = 0;
            while (true)
            {
                var nextCanonicalDot = explicitTag.CanonicalName.IndexOf('.', canonicalEnd);
                var nextDisplayDot = explicitTag.DisplayName.IndexOf('.', displayEnd);
                var canonicalLength = nextCanonicalDot < 0 ? explicitTag.CanonicalName.Length : nextCanonicalDot;
                var displayLength = nextDisplayDot < 0 ? explicitTag.DisplayName.Length : nextDisplayDot;
                var canonicalPrefix = explicitTag.CanonicalName.Substring(0, canonicalLength);
                var displayPrefix = explicitTag.DisplayName.Substring(0, displayLength);
                if (!nodes.TryGetValue(canonicalPrefix, out var node))
                {
                    nodes.Add(canonicalPrefix, new BuildNode(canonicalPrefix, displayPrefix));
                }
                else if (nextCanonicalDot < 0)
                {
                    node.DisplayName = explicitTag.DisplayName;
                }

                if (nextCanonicalDot < 0) return;
                canonicalEnd = nextCanonicalDot + 1;
                displayEnd = nextDisplayDot + 1;
            }
        }

        private static void FillPreorder(
            BuildNode node,
            ushort parent,
            string[] displayNames,
            ushort[] parents,
            ushort[] subtreeEnds,
            Dictionary<string, ushort> byCanonicalName,
            ref int nextIndex)
        {
            foreach (var child in node.GetChildrenInOrdinalOrder())
            {
                var index = checked((ushort)nextIndex++);
                displayNames[index] = child.DisplayName;
                parents[index] = parent;
                byCanonicalName.Add(child.CanonicalName, index);
                FillPreorder(child, index, displayNames, parents, subtreeEnds, byCanonicalName, ref nextIndex);
                subtreeEnds[index] = checked((ushort)(nextIndex - 1));
            }
        }

        private readonly struct ExplicitTag
        {
            internal ExplicitTag(string canonicalName, string displayName)
            {
                CanonicalName = canonicalName;
                DisplayName = displayName;
            }

            internal string CanonicalName { get; }
            internal string DisplayName { get; }
        }

        private sealed class BuildNode
        {
            internal BuildNode(string canonicalName, string displayName)
            {
                CanonicalName = canonicalName;
                DisplayName = displayName;
            }

            internal string CanonicalName { get; }
            internal string DisplayName { get; set; }
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
