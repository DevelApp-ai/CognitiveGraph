/*
 * CognitiveGraph - Zero-Copy Cognitive Graph for Advanced Code Analysis
 * Copyright (C) 2024 DevelApp-ai
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
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
using System.Linq;
using System.Runtime.InteropServices;
using CognitiveGraph.Accessors;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph;

/// <summary>
/// High-performance editor for creating modified versions of existing CognitiveGraphs.
/// Uses a queue-and-rebuild pattern for efficient batch operations.
/// </summary>
/// <remarks>
/// <see cref="Build"/> re-emits every reachable symbol node, packed node, child list,
/// property and CPG edge through a <see cref="CognitiveGraphBuilder"/>, applying the
/// queued operations as each target node is encountered. Shared nodes are rebuilt
/// exactly once (memoized by source offset) so the SPPF sharing structure of the
/// source graph is preserved, and in-progress guards reject cyclic (malformed)
/// graphs instead of recursing forever. Deleted nodes are signalled with a
/// <c>null</c> rebuild result instead of a magic offset, and are dropped from
/// every child list and CPG edge that referenced them.
/// Only V1 (Compact) graphs are supported.
/// </remarks>
public sealed class CognitiveGraphEditor : IDisposable
{
    private readonly CognitiveGraph _sourceGraph;
    private readonly List<EditOperation> _operations;
    private readonly Dictionary<uint, uint> _offsetMapping; // Old offset -> New offset

    // Rebuild state (reset at the start of every Build call)
    private Dictionary<uint, List<EditOperation>> _operationsByOffset = new();
    private CognitiveGraphBuilder? _builder;
    private readonly Dictionary<uint, uint> _symbolNodeMap = new();   // Source offset -> rebuilt offset
    private readonly Dictionary<uint, uint> _packedNodeMap = new();   // Source offset -> rebuilt offset
    private readonly HashSet<uint> _deletedSymbolNodes = new();
    private readonly HashSet<uint> _inProgressSymbolNodes = new();
    private readonly HashSet<uint> _inProgressPackedNodes = new();
    private bool _disposed;

    public CognitiveGraphEditor(CognitiveGraph sourceGraph)
    {
        _sourceGraph = sourceGraph ?? throw new ArgumentNullException(nameof(sourceGraph));
        _operations = new List<EditOperation>();
        _offsetMapping = new Dictionary<uint, uint>();
    }

    /// <summary>
    /// Queues an insert node operation
    /// </summary>
    public CognitiveGraphEditor InsertNode(uint targetOffset, ushort symbolId, ushort nodeType,
        uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null,
        IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
    {
        var operation = new InsertNodeOperation(targetOffset, symbolId, nodeType, sourceStart, sourceLength, packedNodeOffsets, properties);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Queues a replace node operation
    /// </summary>
    public CognitiveGraphEditor ReplaceNode(uint targetOffset, ushort newSymbolId, ushort newNodeType,
        uint newSourceStart, uint newSourceLength,
        IReadOnlyList<uint>? newPackedNodeOffsets = null,
        IReadOnlyList<(string key, PropertyValueType type, object value)>? newProperties = null)
    {
        var operation = new ReplaceNodeOperation(targetOffset, newSymbolId, newNodeType, newSourceStart, newSourceLength, newPackedNodeOffsets, newProperties);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Queues a delete node operation
    /// </summary>
    public CognitiveGraphEditor DeleteNode(uint targetOffset)
    {
        var operation = new DeleteNodeOperation(targetOffset);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Queues a move node operation
    /// </summary>
    public CognitiveGraphEditor MoveNode(uint targetOffset, uint newSourceStart, uint newSourceLength)
    {
        var operation = new MoveNodeOperation(targetOffset, newSourceStart, newSourceLength);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Queues an update property operation
    /// </summary>
    public CognitiveGraphEditor UpdateProperty(uint targetOffset, string propertyKey, PropertyValueType propertyType, object propertyValue)
    {
        var operation = new UpdatePropertyOperation(targetOffset, propertyKey, propertyType, propertyValue);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Queues a remove property operation
    /// </summary>
    public CognitiveGraphEditor RemoveProperty(uint targetOffset, string propertyKey)
    {
        var operation = new UpdatePropertyOperation(targetOffset, propertyKey);
        _operations.Add(operation);
        return this;
    }

    /// <summary>
    /// Builds the modified graph by applying all queued operations
    /// </summary>
    public CognitiveGraph Build()
    {
        ThrowIfDisposed();

        if (_sourceGraph.SchemaVersion != SchemaVersion.V1)
            throw new InvalidOperationException("CognitiveGraphEditor currently supports V1 (Compact) graphs only.");

        ResetRebuildState();

        if (_operations.Count == 0)
        {
            // No operations, return a true copy of the original
            return CloneOriginalGraph();
        }

        _operationsByOffset = _operations
            .GroupBy(op => op.TargetOffset)
            .ToDictionary(g => g.Key, g => g.ToList());

        using var builder = _builder = new CognitiveGraphBuilder();

        // Rebuild the graph, applying operations as we encounter the target nodes
        var newRootOffset = RebuildSymbolNode(GetRootOffset())
            ?? throw new InvalidOperationException(
                "The root node cannot be deleted: the resulting graph would be empty. Delete individual child nodes instead.");

        // Apply any insert operations that don't target existing nodes.
        // Packed node offsets supplied by the caller refer to the source graph
        // and are rebuilt (with their children and edges) into the new graph.
        foreach (var insertOp in _operations.OfType<InsertNodeOperation>()
                     .Where(op => !_operationsByOffset.ContainsKey(op.TargetOffset)))
        {
            var packedNodeOffsets = RebuildPackedNodeList(insertOp.PackedNodeOffsets);
            var newOffset = builder.WriteSymbolNode(
                insertOp.SymbolId,
                insertOp.NodeType,
                insertOp.SourceStart,
                insertOp.SourceLength,
                packedNodeOffsets,
                insertOp.Properties);
            _offsetMapping[insertOp.TargetOffset] = newOffset;
        }

        var buffer = builder.Build(newRootOffset, _sourceGraph.GetSourceText());
        _builder = null;
        return new CognitiveGraph(buffer);
    }

    /// <summary>
    /// Recursively rebuilds the symbol node at the given source offset, applying
    /// any queued operations that target it. Returns <c>null</c> when the node is
    /// deleted, so callers can drop it from child lists and CPG edges. Shared
    /// nodes are rebuilt exactly once; cycles in malformed graphs are rejected.
    /// </summary>
    private uint? RebuildSymbolNode(uint nodeOffset)
    {
        if (_symbolNodeMap.TryGetValue(nodeOffset, out var existingOffset))
            return existingOffset;
        if (_deletedSymbolNodes.Contains(nodeOffset))
            return null;
        if (!_inProgressSymbolNodes.Add(nodeOffset))
            throw new InvalidOperationException($"Cycle detected at symbol node offset {nodeOffset}.");

        var sourceNode = _sourceGraph.GetNodeAt(nodeOffset);

        // Start from the source node's own data; operations mutate this state in queue order.
        var symbolId = sourceNode.SymbolID;
        var nodeType = sourceNode.NodeType;
        var sourceStart = sourceNode.SourceStart;
        var sourceLength = sourceNode.SourceLength;
        var properties = ExtractNodeProperties(sourceNode);
        IReadOnlyList<uint>? packedNodeOverride = null;
        var deleted = false;

        if (_operationsByOffset.TryGetValue(nodeOffset, out var operations))
        {
            foreach (var operation in operations)
            {
                switch (operation)
                {
                    case DeleteNodeOperation:
                        // Skip this node (and, transitively, its subtree) entirely
                        deleted = true;
                        break;

                    case ReplaceNodeOperation replaceOp:
                        symbolId = replaceOp.NewSymbolId;
                        nodeType = replaceOp.NewNodeType;
                        sourceStart = replaceOp.NewSourceStart;
                        sourceLength = replaceOp.NewSourceLength;
                        properties = replaceOp.NewProperties != null
                            ? new List<(string key, PropertyValueType type, object value)>(replaceOp.NewProperties)
                            : properties;
                        packedNodeOverride = replaceOp.NewPackedNodeOffsets;
                        break;

                    case MoveNodeOperation moveOp:
                        // Update source position; packed nodes, children and
                        // properties are preserved by the rebuild below
                        sourceStart = moveOp.NewSourceStart;
                        sourceLength = moveOp.NewSourceLength;
                        break;

                    case UpdatePropertyOperation updateProp:
                        // Remove any existing property with the same key, then add
                        // the updated one (or leave it removed for RemoveProperty)
                        properties.RemoveAll(p => p.key == updateProp.PropertyKey);
                        if (!updateProp.RemoveProperty)
                            properties.Add((updateProp.PropertyKey, updateProp.PropertyType, updateProp.PropertyValue));
                        break;

                    case InsertNodeOperation:
                        // Inserts that target an existing node are no-ops for that
                        // node; stand-alone inserts are handled in Build()
                        break;
                }
            }
        }

        if (deleted)
        {
            _inProgressSymbolNodes.Remove(nodeOffset);
            _deletedSymbolNodes.Add(nodeOffset);
            return null;
        }

        // Rebuild packed nodes (derivations) with their children and CPG edges.
        // A ReplaceNodeOperation can override the packed node list; supplied
        // offsets refer to the source graph and are remapped to the new one.
        List<uint>? packedNodeOffsets = packedNodeOverride != null
            ? RebuildPackedNodeList(packedNodeOverride)
            : RebuildPackedNodeList(ReadOffsetList(sourceNode.PackedNodesOffset));

        var newOffset = _builder!.WriteSymbolNode(
            symbolId,
            nodeType,
            sourceStart,
            sourceLength,
            packedNodeOffsets,
            properties.Count > 0 ? properties : null);

        _inProgressSymbolNodes.Remove(nodeOffset);
        _symbolNodeMap[nodeOffset] = newOffset;
        _offsetMapping[nodeOffset] = newOffset;
        return newOffset;
    }

    /// <summary>
    /// Recursively rebuilds the packed node at the given source offset: child
    /// symbol nodes are rebuilt first (deleted children are dropped), then CPG
    /// edges are re-targeted to the rebuilt nodes (edges to deleted nodes are
    /// dropped). Shared packed nodes are rebuilt exactly once.
    /// </summary>
    private uint? RebuildPackedNode(uint packedNodeOffset)
    {
        if (_packedNodeMap.TryGetValue(packedNodeOffset, out var existingOffset))
            return existingOffset;
        if (!_inProgressPackedNodes.Add(packedNodeOffset))
            throw new InvalidOperationException($"Cycle detected at packed node offset {packedNodeOffset}.");

        var packedNode = GetPackedNodeAt(packedNodeOffset);

        // Rebuild child symbol nodes, dropping deleted ones
        List<uint>? childNodeOffsets = null;
        var sourceChildOffsets = ReadOffsetList(packedNode.ChildNodesOffset);
        if (sourceChildOffsets.Count > 0)
        {
            childNodeOffsets = new List<uint>(sourceChildOffsets.Count);
            foreach (var childOffset in sourceChildOffsets)
            {
                if (RebuildSymbolNode(childOffset) is { } newChildOffset)
                    childNodeOffsets.Add(newChildOffset);
            }
            if (childNodeOffsets.Count == 0)
                childNodeOffsets = null;
        }

        // Rebuild CPG edges, re-targeting each edge to the rebuilt target node
        List<CpgEdgeData>? cpgEdges = null;
        var edges = packedNode.GetCpgEdges();
        if (edges.Count > 0)
        {
            cpgEdges = new List<CpgEdgeData>(edges.Count);
            foreach (var edge in edges)
            {
                if (RebuildSymbolNode(edge.TargetNodeOffset) is not { } newTargetOffset)
                    continue; // Target node was deleted; drop the edge

                var edgeProperties = ExtractProperties(edge.GetProperties());
                cpgEdges.Add(new CpgEdgeData(
                    (ushort)edge.EdgeType,
                    newTargetOffset,
                    edgeProperties.Count > 0 ? (uint)_builder!.WritePropertyList(edgeProperties) : 0u));
            }
            if (cpgEdges.Count == 0)
                cpgEdges = null;
        }

        var newPackedOffset = _builder!.WritePackedNode(packedNode.RuleID, childNodeOffsets, cpgEdges);

        _inProgressPackedNodes.Remove(packedNodeOffset);
        _packedNodeMap[packedNodeOffset] = newPackedOffset;
        return newPackedOffset;
    }

    /// <summary>
    /// Rebuilds a list of source-graph packed node offsets into the new graph,
    /// dropping deleted ones. Returns <c>null</c> when nothing remains.
    /// </summary>
    private List<uint>? RebuildPackedNodeList(IReadOnlyList<uint>? sourceOffsets)
    {
        if (sourceOffsets == null || sourceOffsets.Count == 0)
            return null;

        var newOffsets = new List<uint>(sourceOffsets.Count);
        foreach (var packedOffset in sourceOffsets)
        {
            if (RebuildPackedNode(packedOffset) is { } newPackedOffset)
                newOffsets.Add(newPackedOffset);
        }
        return newOffsets.Count > 0 ? newOffsets : null;
    }

    /// <summary>
    /// Extracts all properties from a node into a list
    /// </summary>
    private static List<(string key, PropertyValueType type, object value)> ExtractNodeProperties(SymbolNode node)
        => ExtractProperties(node.GetProperties());

    /// <summary>
    /// Boxes every property of a collection into a builder-compatible list.
    /// All <see cref="PropertyValueType"/> variants are supported.
    /// </summary>
    private static List<(string key, PropertyValueType type, object value)> ExtractProperties(PropertyCollection collection)
    {
        var properties = new List<(string key, PropertyValueType type, object value)>(collection.Count);
        foreach (var property in collection)
        {
            var value = property.GetValue();
            object boxed = value.Type switch
            {
                PropertyValueType.String => value.AsString(),
                PropertyValueType.Int32 => value.AsInt32(),
                PropertyValueType.UInt32 => value.AsUInt32(),
                PropertyValueType.Int64 => value.AsInt64(),
                PropertyValueType.UInt64 => value.AsUInt64(),
                PropertyValueType.Float => value.AsFloat(),
                PropertyValueType.Double => value.AsDouble(),
                PropertyValueType.Boolean => value.AsBoolean(),
                PropertyValueType.Binary => value.AsBinary().ToArray(),
                _ => throw new NotSupportedException(
                    $"Property '{property.GetKey()}' uses unsupported value type {value.Type}.")
            };
            properties.Add((property.GetKey(), value.Type, boxed));
        }
        return properties;
    }

    /// <summary>
    /// Creates a true copy of the original graph when no operations are queued.
    /// Every symbol node, packed node, child list, property and CPG edge is
    /// re-emitted into a fresh buffer; sharing is preserved by the memo tables.
    /// </summary>
    private CognitiveGraph CloneOriginalGraph()
    {
        using var builder = _builder = new CognitiveGraphBuilder();

        var newRootOffset = RebuildSymbolNode(GetRootOffset())
            ?? throw new InvalidOperationException("The source graph has no root node.");

        var buffer = builder.Build(newRootOffset, _sourceGraph.GetSourceText());
        _builder = null;
        return new CognitiveGraph(buffer);
    }

    /// <summary>
    /// Gets the packed node accessor for a source-graph offset
    /// </summary>
    private PackedNode GetPackedNodeAt(uint packedNodeOffset)
    {
        var buffer = _sourceGraph.GetBufferV1()
            ?? throw new InvalidOperationException("Failed to access the V1 graph buffer.");
        var span = buffer.Slice((int)packedNodeOffset, PackedNodeData.SIZE);
        return new PackedNode(span, buffer);
    }

    /// <summary>
    /// Reads a source-graph list of uint offsets ([uint count][uint items...])
    /// </summary>
    private List<uint> ReadOffsetList(uint listOffset)
    {
        if (listOffset == 0)
            return new List<uint>();

        var buffer = _sourceGraph.GetBufferV1()
            ?? throw new InvalidOperationException("Failed to access the V1 graph buffer.");
        var listSpan = buffer.GetListSpan(listOffset, sizeof(uint));
        var offsets = new List<uint>(listSpan.Length / sizeof(uint));
        for (var i = 0; i < listSpan.Length / sizeof(uint); i++)
            offsets.Add(MemoryMarshal.Read<uint>(listSpan.Slice(i * sizeof(uint))));
        return offsets;
    }

    /// <summary>
    /// Gets the root node offset of the source graph
    /// </summary>
    private uint GetRootOffset()
    {
        var header = _sourceGraph.GetHeader()
            ?? throw new InvalidOperationException("CognitiveGraphEditor requires a V1 (Compact) graph with a header.");
        return header.RootNodeOffset;
    }

    /// <summary>
    /// Clears all rebuild state so Build can be invoked again on the same editor
    /// </summary>
    private void ResetRebuildState()
    {
        _operationsByOffset = new Dictionary<uint, List<EditOperation>>();
        _builder = null;
        _symbolNodeMap.Clear();
        _packedNodeMap.Clear();
        _deletedSymbolNodes.Clear();
        _inProgressSymbolNodes.Clear();
        _inProgressPackedNodes.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CognitiveGraphEditor));
    }

    /// <summary>
    /// Gets the number of queued operations
    /// </summary>
    public int OperationCount => _operations.Count;

    /// <summary>
    /// Clears all queued operations
    /// </summary>
    public void ClearOperations()
    {
        _operations.Clear();
        _offsetMapping.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _operations.Clear();
            _offsetMapping.Clear();
            ResetRebuildState();
            _disposed = true;
        }
    }
}
