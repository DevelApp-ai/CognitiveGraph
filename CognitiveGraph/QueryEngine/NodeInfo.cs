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

using System;
using System.Collections.Generic;
using CognitiveGraph.Accessors;
using CognitiveGraph.Schema;

namespace CognitiveGraph.QueryEngine;

/// <summary>
/// Materialized snapshot of a symbol node, suitable for GraphQL resolvers
/// (the zero-copy <see cref="SymbolNode"/> accessor is a ref struct and cannot
/// be stored or used as a GraphQL source type).
/// </summary>
public sealed class NodeInfo
{
    public uint Offset { get; }
    public ushort SymbolId { get; }
    public ushort NodeType { get; }
    public uint SourceStart { get; }
    public uint SourceLength { get; }
    public bool IsAmbiguous { get; }

    public NodeInfo(uint offset, ushort symbolId, ushort nodeType, uint sourceStart, uint sourceLength, bool isAmbiguous)
    {
        Offset = offset;
        SymbolId = symbolId;
        NodeType = nodeType;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        IsAmbiguous = isAmbiguous;
    }
}

/// <summary>
/// Materialized key/type/value snapshot of a node or edge property.
/// The value is rendered as its string form.
/// </summary>
public sealed class NodeProperty
{
    public string Key { get; }
    public string Type { get; }
    public string Value { get; }

    public NodeProperty(string key, string type, string value)
    {
        Key = key;
        Type = type;
        Value = value;
    }
}

/// <summary>
/// Materialized snapshot of a packed node (one SPPF derivation alternative).
/// </summary>
public sealed class PackedNodeInfo
{
    public uint Offset { get; }
    public ushort RuleId { get; }

    /// <summary>
    /// Buffer offset of the symbol node that owns this packed node.
    /// Carried on the snapshot so resolvers do not depend on parent-context walking.
    /// </summary>
    public uint OwnerSymbolOffset { get; }

    public PackedNodeInfo(uint offset, ushort ruleId, uint ownerSymbolOffset)
    {
        Offset = offset;
        RuleId = ruleId;
        OwnerSymbolOffset = ownerSymbolOffset;
    }
}

/// <summary>
/// Materialized snapshot of a CPG edge.
/// </summary>
public sealed class EdgeInfo
{
    public int EdgeType { get; }
    public uint TargetNodeOffset { get; }

    /// <summary>
    /// Buffer offsets of the symbol node and packed node that own this edge.
    /// Carried on the snapshot so resolvers do not depend on parent-context walking.
    /// </summary>
    public uint OwnerSymbolOffset { get; }
    public uint OwnerPackedNodeOffset { get; }

    public EdgeInfo(int edgeType, uint targetNodeOffset, uint ownerSymbolOffset, uint ownerPackedNodeOffset)
    {
        EdgeType = edgeType;
        TargetNodeOffset = targetNodeOffset;
        OwnerSymbolOffset = ownerSymbolOffset;
        OwnerPackedNodeOffset = ownerPackedNodeOffset;
    }
}

/// <summary>
/// Traversal and materialization helpers over a V1 CognitiveGraph.
/// All methods are safe to call from GraphQL resolvers; the ref-struct accessors
/// they use never escape the method body.
/// </summary>
internal static class GraphTraversal
{
    /// <summary>
    /// Materializes the symbol node at the given buffer offset.
    /// </summary>
    public static NodeInfo Snapshot(CognitiveGraph graph, uint offset)
    {
        var node = graph.GetNodeAt(offset);
        return new NodeInfo(offset, node.SymbolID, node.NodeType, node.SourceStart, node.SourceLength, node.IsAmbiguous);
    }

    /// <summary>
    /// Materializes the source text covered by the node at the given offset.
    /// </summary>
    public static string SourceText(CognitiveGraph graph, uint offset)
    {
        return graph.GetNodeAt(offset).GetSourceText().ToString();
    }

    /// <summary>
    /// Materializes all properties of the node at the given offset.
    /// </summary>
    public static List<NodeProperty> GetProperties(CognitiveGraph graph, uint offset)
    {
        var result = new List<NodeProperty>();
        foreach (var property in graph.GetNodeAt(offset).GetProperties())
        {
            result.Add(new NodeProperty(
                property.GetKey(),
                property.GetValue().Type.ToString(),
                RenderValue(property.GetValue())));
        }

        return result;
    }

    private static string RenderValue(PropertyValue value)
    {
        return value.Type switch
        {
            PropertyValueType.String => value.AsString(),
            PropertyValueType.Int32 => value.AsInt32().ToString(),
            PropertyValueType.UInt32 => value.AsUInt32().ToString(),
            PropertyValueType.Int64 => value.AsInt64().ToString(),
            PropertyValueType.UInt64 => value.AsUInt64().ToString(),
            PropertyValueType.Float => value.AsFloat().ToString(),
            PropertyValueType.Double => value.AsDouble().ToString(),
            PropertyValueType.Boolean => value.AsBoolean().ToString(),
            PropertyValueType.Binary => Convert.ToBase64String(value.AsBinary().ToArray()),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Materializes all packed nodes (derivations) of the symbol node at the given offset,
    /// including their buffer offsets so children and edges can be resolved lazily.
    /// </summary>
    public static List<PackedNodeInfo> GetPackedNodes(CognitiveGraph graph, uint offset)
    {
        var result = new List<PackedNodeInfo>();
        var packedNodes = graph.GetNodeAt(offset).GetPackedNodes();

        for (int i = 0; i < packedNodes.Count; i++)
        {
            result.Add(new PackedNodeInfo(PackedNodeOffsetAt(graph, offset, i), packedNodes[i].RuleID, offset));
        }

        return result;
    }

    /// <summary>
    /// Materializes the child symbol nodes of a specific packed node.
    /// </summary>
    public static List<NodeInfo> GetChildrenOfPackedNode(CognitiveGraph graph, uint symbolOffset, uint packedNodeOffset)
    {
        var result = new List<NodeInfo>();
        var packedNode = FindPackedNode(graph, symbolOffset, packedNodeOffset);

        foreach (var child in packedNode.GetChildNodes())
        {
            result.Add(new NodeInfo(child.Offset, child.SymbolID, child.NodeType, child.SourceStart, child.SourceLength, child.IsAmbiguous));
        }

        return result;
    }

    /// <summary>
    /// Materializes the CPG edges attached to a specific packed node.
    /// </summary>
    public static List<EdgeInfo> GetEdgesOfPackedNode(CognitiveGraph graph, uint symbolOffset, uint packedNodeOffset)
    {
        var result = new List<EdgeInfo>();
        var packedNode = FindPackedNode(graph, symbolOffset, packedNodeOffset);

        foreach (var edge in packedNode.GetCpgEdges())
        {
            result.Add(new EdgeInfo((int)edge.EdgeType, edge.TargetNodeOffset, symbolOffset, packedNodeOffset));
        }

        return result;
    }

    /// <summary>
    /// Materializes the properties of the CPG edge (within the given packed node)
    /// that targets <paramref name="targetNodeOffset"/>.
    /// </summary>
    public static List<NodeProperty> GetEdgeProperties(CognitiveGraph graph, uint symbolOffset, uint packedNodeOffset, uint targetNodeOffset)
    {
        var packedNode = FindPackedNode(graph, symbolOffset, packedNodeOffset);

        foreach (var edge in packedNode.GetCpgEdges())
        {
            if (edge.TargetNodeOffset != targetNodeOffset)
                continue;

            var result = new List<NodeProperty>();
            foreach (var property in edge.GetProperties())
            {
                result.Add(new NodeProperty(
                    property.GetKey(),
                    property.GetValue().Type.ToString(),
                    RenderValue(property.GetValue())));
            }

            return result;
        }

        return new List<NodeProperty>();
    }

    /// <summary>
    /// Materializes all child symbol nodes across every packed node (derivation)
    /// of the symbol node at the given offset. Shared children appear once per
    /// derivation that references them.
    /// </summary>
    public static List<NodeInfo> GetChildren(CognitiveGraph graph, uint offset)
    {
        var result = new List<NodeInfo>();

        foreach (var packedNode in graph.GetNodeAt(offset).GetPackedNodes())
        {
            foreach (var child in packedNode.GetChildNodes())
            {
                result.Add(new NodeInfo(child.Offset, child.SymbolID, child.NodeType, child.SourceStart, child.SourceLength, child.IsAmbiguous));
            }
        }

        return result;
    }

    /// <summary>
    /// Enumerates every symbol node reachable from the root through packed-node
    /// child edges, in depth-first order. Guards against cycles in malformed
    /// graphs. Returns the root first.
    /// </summary>
    public static List<NodeInfo> GetAllNodes(CognitiveGraph graph)
    {
        var result = new List<NodeInfo>();
        var visited = new HashSet<uint>();
        var stack = new Stack<uint>();
        stack.Push(graph.GetRootNode().Offset);

        while (stack.Count > 0)
        {
            var offset = stack.Pop();
            if (!visited.Add(offset))
                continue;

            var node = graph.GetNodeAt(offset);
            result.Add(new NodeInfo(offset, node.SymbolID, node.NodeType, node.SourceStart, node.SourceLength, node.IsAmbiguous));

            // Push children in reverse so depth-first traversal visits them in list order.
            var childOffsets = new List<uint>();
            foreach (var packedNode in node.GetPackedNodes())
            {
                foreach (var child in packedNode.GetChildNodes())
                {
                    childOffsets.Add(child.Offset);
                }
            }

            for (int i = childOffsets.Count - 1; i >= 0; i--)
            {
                if (!visited.Contains(childOffsets[i]))
                    stack.Push(childOffsets[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// The PackedNode ref-struct accessor does not carry its own buffer offset, so the
    /// owning symbol node's packed-node list is re-walked by index to find the packed
    /// node stored at <paramref name="packedNodeOffset"/>.
    /// </summary>
    private static PackedNode FindPackedNode(CognitiveGraph graph, uint symbolOffset, uint packedNodeOffset)
    {
        var packedNodes = graph.GetNodeAt(symbolOffset).GetPackedNodes();

        for (int i = 0; i < packedNodes.Count; i++)
        {
            if (PackedNodeOffsetAt(graph, symbolOffset, i) == packedNodeOffset)
                return packedNodes[i];
        }

        throw new InvalidOperationException(
            $"Packed node at offset {packedNodeOffset} is not owned by the symbol node at offset {symbolOffset}.");
    }

    /// <summary>
    /// Reads the i-th entry of the symbol node's packed-node offset list
    /// (list layout: [uint count][uint offsets...]).
    /// </summary>
    private static uint PackedNodeOffsetAt(CognitiveGraph graph, uint symbolOffset, int index)
    {
        var node = graph.GetNodeAt(symbolOffset);
        var listOffset = node.PackedNodesOffset;
        if (listOffset == 0)
            throw new InvalidOperationException("Symbol node has no packed nodes.");

        var buffer = graph.GetBufferV1()
            ?? throw new InvalidOperationException("Packed node offset lists require a V1 graph buffer.");

        return buffer.Read<uint>(listOffset + sizeof(uint) + (uint)(index * sizeof(uint)));
    }
}
