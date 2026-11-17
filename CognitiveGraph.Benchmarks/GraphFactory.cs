/*
 * CognitiveGraph - Zero-Copy Cognitive Graph for Advanced Code Analysis
 * Copyright (C) 2024 DevelApp-ai
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using System.Text;
using CognitiveGraph.Accessors;
using CogGraph = CognitiveGraph.CognitiveGraph;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Builds the same AST-shaped tree in all three representations
/// (CognitiveGraph buffer, naive mutable object tree, persistent immutable
/// tree) so the benchmarks compare like with like.
///
/// Tree shape: complete binary tree in level-order indexing — node i has
/// children 2i+1 and 2i+2 (when in range). Every node is a symbol node with
/// two properties ("NodeType": string, "line": int) — the same property
/// payload the library's own allocation tests use. Internal nodes carry a
/// packed node linking their children, so both symbol nodes and packed
/// nodes are exercised (SPPF-style packing + child links).
/// </summary>
public static class GraphFactory
{
    public const string NodeTypeKey = "NodeType";
    public const string LineKey = "line";

    public static List<(string key, PropertyValueType type, object value)> MakeProperties(int i) =>
        new()
        {
            (NodeTypeKey, PropertyValueType.String, "TestNode"),
            (LineKey, PropertyValueType.Int32, i)
        };

    public static string MakeSourceText(int nodeCount) =>
        new string('a', Math.Max(8, nodeCount * 8));

    /// <summary>
    /// Builds a nodeCount-node tree into the supplied builder.
    /// Returns the per-node symbol offsets (level order) and the root offset.
    /// </summary>
    public static uint BuildIntoBuilder(CognitiveGraphBuilder builder, int nodeCount,
        out uint[] symbolOffsets)
    {
        symbolOffsets = new uint[nodeCount];

        // Post-order: children before parents so child offsets are known.
        for (int i = nodeCount - 1; i >= 0; i--)
        {
            uint packedOffset = 0;
            int left = 2 * i + 1;
            if (left < nodeCount)
            {
                var childOffsets = new List<uint> { symbolOffsets[left] };
                int right = 2 * i + 2;
                if (right < nodeCount)
                    childOffsets.Add(symbolOffsets[right]);
                packedOffset = builder.WritePackedNode((ushort)(i % 7 + 1), childOffsets);
            }

            symbolOffsets[i] = builder.WriteSymbolNode(
                symbolId: (ushort)(i % 512 + 1),
                nodeType: (ushort)(i % 32 + 100),
                sourceStart: (uint)(i * 8),
                sourceLength: 5u,
                packedNodeOffsets: packedOffset != 0 ? new List<uint> { packedOffset } : null,
                properties: MakeProperties(i));
        }

        return symbolOffsets[0];
    }

    /// <summary>
    /// Builds a nodeCount-node CognitiveGraph (V1) and returns the graph plus
    /// the level-order symbol offsets. Caller disposes the graph.
    /// </summary>
    public static CogGraph BuildCognitiveGraph(int nodeCount,
        int initialCapacityBytes, out uint[] symbolOffsets)
    {
        var options = new GraphBuilderOptions
        {
            Schema = SchemaVersion.V1,
            InitialCapacity = initialCapacityBytes
        };
        using var builder = new CognitiveGraphBuilder(options);
        uint root = BuildIntoBuilder(builder, nodeCount, out symbolOffsets);
        var buffer = builder.Build(root, MakeSourceText(nodeCount));
        return new CogGraph(buffer);
    }

    /// <summary>
    /// Builds the same tree as a naive mutable object AST.
    /// Returns the root and the level-order node array.
    /// </summary>
    public static NaiveObjectNode BuildNaiveTree(int nodeCount, out NaiveObjectNode[] nodes)
    {
        nodes = new NaiveObjectNode[nodeCount];
        for (int i = nodeCount - 1; i >= 0; i--)
        {
            var node = new NaiveObjectNode
            {
                SymbolId = (ushort)(i % 512 + 1),
                NodeType = (ushort)(i % 32 + 100),
                SourceStart = (uint)(i * 8),
                SourceLength = 5u
            };
            node.Properties[NodeTypeKey] = "TestNode";
            node.Properties[LineKey] = i;

            int left = 2 * i + 1;
            if (left < nodeCount)
            {
                node.Children.Add(nodes[left]);
                int right = 2 * i + 2;
                if (right < nodeCount)
                    node.Children.Add(nodes[right]);
            }
            nodes[i] = node;
        }
        return nodes[0];
    }

    /// <summary>
    /// Builds the same tree as a persistent immutable AST. Returns the root
    /// and the level-order node array.
    /// </summary>
    public static PersistentNode BuildPersistentTree(int nodeCount, out PersistentNode[] nodes)
    {
        nodes = new PersistentNode[nodeCount];
        for (int i = nodeCount - 1; i >= 0; i--)
        {
            var properties = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [NodeTypeKey] = "TestNode",
                [LineKey] = i
            };

            PersistentNode[] children = Array.Empty<PersistentNode>();
            int left = 2 * i + 1;
            int right = 2 * i + 2;
            if (left < nodeCount)
            {
                children = new PersistentNode[right < nodeCount ? 2 : 1];
                children[0] = nodes[left];
                if (right < nodeCount)
                    children[1] = nodes[right];
            }

            nodes[i] = new PersistentNode(
                (ushort)(i % 512 + 1), (ushort)(i % 32 + 100),
                (uint)(i * 8), 5u, children, properties);
        }
        return nodes[0];
    }

    /// <summary>
    /// Index of the deep target node used by the edit benchmarks
    /// (bottom two levels of the tree).
    /// </summary>
    public static int EditTargetIndex(int nodeCount) => nodeCount / 2 + 1;

    /// <summary>
    /// Walks every symbol node of the graph (headers + property values),
    /// forcing all mmap pages in. Returns a checksum.
    /// </summary>
    public static long Traverse(CogGraph graph, uint[] symbolOffsets)
    {
        long sum = 0;
        foreach (uint offset in symbolOffsets)
        {
            SymbolNode node = graph.GetNodeAt(offset);
            sum += node.SymbolID + node.SourceStart;
            foreach (var property in node.GetProperties())
            {
                var value = property.GetValue();
                if (value.Type == PropertyValueType.Int32)
                    sum += value.AsInt32();
            }
        }
        return sum;
    }
}
