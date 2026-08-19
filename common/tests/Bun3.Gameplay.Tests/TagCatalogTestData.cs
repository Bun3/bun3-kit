using System;
using System.IO;
using System.Text;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Tests;

internal static class TagCatalogTestData
{
    internal const string CanonicalJson = """
    {
      "schemaVersion": 1,
      "tags": [
        { "name": "State.Rooted", "comment": "cannot move" },
        { "name": "ability.movement.Jump", "comment": "jump" },
        { "name": "State.Dead.Ghost", "comment": "ghost" }
      ],
      "redirects": []
    }
    """;

    internal static TagCatalog Load(string json = CanonicalJson)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false, true).GetBytes(json));
        return TagCatalogJson.Load(stream);
    }

    internal static string BuildFlatCatalog(int count)
    {
        var json = new StringBuilder(count * 20);
        json.Append("{\"schemaVersion\":1,\"tags\":[");
        for (var i = 0; i < count; i++)
        {
            if (i != 0) json.Append(',');
            json.Append("{\"name\":\"T").Append(i).Append("\"}");
        }

        return json.Append("]}").ToString();
    }

    internal static string BuildTwoLevelCatalog(int chainCount, bool includeExtraRoot)
    {
        var json = new StringBuilder(chainCount * 26);
        json.Append("{\"schemaVersion\":1,\"tags\":[");
        for (var i = 0; i < chainCount; i++)
        {
            if (i != 0) json.Append(',');
            json.Append("{\"name\":\"P").Append(i).Append(".Leaf\"}");
        }

        if (includeExtraRoot)
        {
            if (chainCount != 0) json.Append(',');
            json.Append("{\"name\":\"Extra\"}");
        }

        return json.Append("]}").ToString();
    }

    internal static string ChainLeaf(int chain, int depth)
    {
        var path = new StringBuilder().Append('B').Append(chain);
        for (var level = 1; level < depth; level++)
            path.Append(".L").Append(level);
        return path.ToString();
    }

    internal static string BuildChainCatalog(int exactKinds, int depth, bool includeMissRoot)
    {
        var json = new StringBuilder().Append("{\"schemaVersion\":1,\"tags\":[");
        for (var i = 0; i < exactKinds; i++)
        {
            if (i != 0) json.Append(',');
            json.Append("{\"name\":\"").Append(ChainLeaf(i, depth)).Append("\"}");
        }

        if (includeMissRoot)
        {
            if (exactKinds != 0) json.Append(',');
            json.Append("{\"name\":\"ZMiss\"}");
        }

        return json.Append("]}").ToString();
    }

    internal static string BuildPerformanceCatalog(int catalogSize, int exactKinds, int depth)
    {
        if (catalogSize < exactKinds * depth)
            throw new ArgumentOutOfRangeException(nameof(catalogSize));
        var json = new StringBuilder(catalogSize * 24);
        json.Append("{\"schemaVersion\":1,\"tags\":[");
        var written = 0;
        for (var i = 0; i < exactKinds; i++)
        {
            if (written++ != 0) json.Append(',');
            json.Append("{\"name\":\"").Append(ChainLeaf(i, depth)).Append("\"}");
        }
        for (var i = 0; i < catalogSize - exactKinds * depth; i++)
        {
            if (written++ != 0) json.Append(',');
            json.Append("{\"name\":\"F").Append(i).Append("\"}");
        }
        return json.Append("]}").ToString();
    }

    internal static string BuildPath(int length) => new string('A', length);

    internal static string BuildDepth(int depth) =>
        string.Join(".", System.Linq.Enumerable.Repeat("A", depth));
}
