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
using System.Text;
using CognitiveGraph.Accessors;
using CognitiveGraph.Builder;
using CognitiveGraph.Buffer;
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

        // Fast path (issue #68): when every queued operation preserves the
        // buffer layout, patch a single clone of the source buffer in place
        // instead of re-emitting every node through a builder.
        var patchedGraph = TryBuildInPlacePatch();
        if (patchedGraph != null)
            return patchedGraph;

        _operationsByOffset = _operations
            .GroupBy(op => op.TargetOffset)
            .ToDictionary(g => g.Key, g => g.ToList());

        using var builder = _builder = new CognitiveGraphBuilder();

        // Rebuild the graph, applying operations as we encounter the target nodes
        var newRootOffset = RebuildSymbolNode(GetRootOffset())
            ?? throw new InvalidOperationException(
                "The root node cannot be deleted: the resulting graph would be empty. Delete individual child nodes instead.");

        // Apply any insert operations that don't target existing nodes.
        // _symbolNodeMap contains every source-graph node visited by the
        // rebuild above, so a target offset absent from it is a stand-alone
        // insert. (Checking _operationsByOffset is wrong: the insert's own
        // TargetOffset is always present there, which silently dropped every
        // stand-alone insert.) Packed node offsets supplied by the caller
        // refer to the source graph and are rebuilt (with their children and
        // edges) into the new graph.
        foreach (var insertOp in _operations.OfType<InsertNodeOperation>()
                     .Where(op => !_symbolNodeMap.ContainsKey(op.TargetOffset)))
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
    /// Fast path for layout-preserving edits (issue #68). Operations that do
    /// not change the buffer layout — <see cref="ReplaceNodeOperation"/>
    /// without packed-node/property overrides, <see cref="MoveNodeOperation"/>,
    /// and <see cref="UpdatePropertyOperation"/> whose payload keeps its byte
    /// length and type — are applied by cloning the source buffer once and
    /// patching the fixed-size records in place. Every offset in the buffer is
    /// preserved, so the cost is one buffer copy plus O(operations) instead of
    /// a full node-by-node rebuild. When any patched node's source span
    /// changes, the serialized interval tree (the last buffer section) is
    /// rebuilt from the old tree's entries with the patched spans and
    /// rewritten at its original offset — same node count, same serialized
    /// size, no layout change. Returns <c>null</c> when any queued operation
    /// cannot be applied in place; the caller falls back to the full rebuild,
    /// which handles inserts, deletes, property adds/removes and type- or
    /// length-changing updates.
    /// </summary>
    private CognitiveGraph? TryBuildInPlacePatch()
    {
        // 1) Eligibility check: refuse the fast path when any operation can
        //    change the buffer's layout or node set.
        foreach (var operation in _operations)
        {
            var patchable = operation switch
            {
                ReplaceNodeOperation r => r.NewPackedNodeOffsets == null && r.NewProperties == null,
                MoveNodeOperation => true,
                UpdatePropertyOperation u => !u.RemoveProperty,
                _ => false
            };
            if (!patchable)
                return null;
        }

        var sourceBuffer = _sourceGraph.GetBufferV1();
        if (sourceBuffer == null)
            return null;

        var header = _sourceGraph.GetHeader();
        if (header == null)
            return null;
        var treeOffset = header.Value.IntervalTreeOffset;

        // 2) Copy-on-write: clone the source buffer once. This also
        //    materializes any non-owned backing array into a writable one.
        var copy = sourceBuffer.AsSpan().ToArray();
        var span = (Span<byte>)copy;

        // 3) Apply the patches in queue order.
        // nodeOffset -> (new start, new length) for nodes whose span changed.
        var spanChanges = new Dictionary<uint, (uint start, uint length)>();
        foreach (var operation in _operations)
        {
            switch (operation)
            {
                case ReplaceNodeOperation replaceOp:
                {
                    if (!TryPatchNodeRecord(span, operation.TargetOffset,
                            replaceOp.NewSymbolId, replaceOp.NewNodeType,
                            replaceOp.NewSourceStart, replaceOp.NewSourceLength,
                            out var oldStart, out var oldLength))
                        return null;
                    if (replaceOp.NewSourceStart != oldStart || replaceOp.NewSourceLength != oldLength)
                        spanChanges[operation.TargetOffset] = (replaceOp.NewSourceStart, replaceOp.NewSourceLength);
                    break;
                }

                case MoveNodeOperation moveOp:
                {
                    if (!TryPatchNodeRecord(span, operation.TargetOffset,
                            null, null,
                            moveOp.NewSourceStart, moveOp.NewSourceLength,
                            out var oldStart, out var oldLength))
                        return null;
                    if (moveOp.NewSourceStart != oldStart || moveOp.NewSourceLength != oldLength)
                        spanChanges[operation.TargetOffset] = (moveOp.NewSourceStart, moveOp.NewSourceLength);
                    break;
                }

                case UpdatePropertyOperation updateOp:
                {
                    if (!TryPatchPropertyValue(sourceBuffer, span, operation.TargetOffset,
                            updateOp.PropertyKey, updateOp.PropertyType, updateOp.PropertyValue))
                        return null;
                    break;
                }
            }
        }

        // 4) When any source span changed, rebuild the serialized interval
        //    tree with the patched spans and rewrite it at its original offset.
        if (spanChanges.Count > 0 && !TryRewriteIntervalTree(span, treeOffset, spanChanges))
            return null;

        return new CognitiveGraph(new CognitiveGraphBuffer(copy, takeOwnership: true));
    }

    /// <summary>
    /// Patches a <see cref="Schema.SymbolNodeData"/> record in place. The
    /// symbol id and node type are optional (patched only when supplied, so
    /// <see cref="MoveNodeOperation"/> leaves them untouched). Returns the
    /// node's pre-patch source span so callers can detect tree changes.
    /// </summary>
    private static bool TryPatchNodeRecord(Span<byte> span, uint nodeOffset,
        ushort? newSymbolId, ushort? newNodeType,
        uint newSourceStart, uint newSourceLength,
        out uint oldSourceStart, out uint oldSourceLength)
    {
        oldSourceStart = 0;
        oldSourceLength = 0;

        if ((ulong)nodeOffset + SymbolNodeData.SIZE > (ulong)span.Length)
            return false;

        var record = span.Slice((int)nodeOffset, SymbolNodeData.SIZE);
        oldSourceStart = MemoryMarshal.Read<uint>(record.Slice(4));
        oldSourceLength = MemoryMarshal.Read<uint>(record.Slice(8));

        if (newSymbolId.HasValue)
        {
            var symbolId = newSymbolId.GetValueOrDefault();
            MemoryMarshal.Write(record.Slice(0), ref symbolId);
        }
        if (newNodeType.HasValue)
        {
            var nodeType = newNodeType.GetValueOrDefault();
            MemoryMarshal.Write(record.Slice(2), ref nodeType);
        }
        MemoryMarshal.Write(record.Slice(4), ref newSourceStart);
        MemoryMarshal.Write(record.Slice(8), ref newSourceLength);
        return true;
    }

    /// <summary>
    /// Patches an existing property value in place. Succeeds only when the
    /// node has a property with the given key whose type matches and whose
    /// new payload occupies exactly as many bytes as the old one; otherwise
    /// the caller falls back to the full rebuild (which rewrites the layout
    /// for adds, removes and length changes).
    /// </summary>
    private static bool TryPatchPropertyValue(CognitiveGraphBuffer sourceBuffer, Span<byte> span,
        uint nodeOffset, string propertyKey, PropertyValueType propertyType, object propertyValue)
    {
        if ((ulong)nodeOffset + SymbolNodeData.SIZE > (ulong)span.Length)
            return false;

        var propertiesOffset = MemoryMarshal.Read<uint>(span.Slice((int)nodeOffset + 16));
        if (propertiesOffset == 0)
            return false; // No properties: adding one changes the layout

        if ((ulong)propertiesOffset + sizeof(uint) > (ulong)span.Length)
            return false;
        var propertyCount = MemoryMarshal.Read<uint>(span.Slice((int)propertiesOffset));

        // Properties layout: [uint count][PropertyData slots...]
        var slotsOffset = propertiesOffset + sizeof(uint);
        if ((ulong)slotsOffset + (ulong)propertyCount * PropertyData.SIZE > (ulong)span.Length)
            return false;

        for (uint i = 0; i < propertyCount; i++)
        {
            var slot = span.Slice((int)(slotsOffset + i * PropertyData.SIZE), PropertyData.SIZE);
            var keyOffset = MemoryMarshal.Read<uint>(slot);
            var valueOffset = MemoryMarshal.Read<uint>(slot.Slice(4));

            if (!string.Equals(sourceBuffer.ReadString(keyOffset), propertyKey, StringComparison.Ordinal))
                continue;

            // Found the property: patch only when type matches and the payload
            // length is unchanged.
            if ((ulong)valueOffset + PropertyValueHeader.SIZE > (ulong)span.Length)
                return false;
            var headerSpan = span.Slice((int)valueOffset, PropertyValueHeader.SIZE);
            var existingType = (PropertyValueType)MemoryMarshal.Read<ushort>(headerSpan);
            var existingLength = MemoryMarshal.Read<uint>(headerSpan.Slice(4));
            if (existingType != propertyType)
                return false; // Type change rewrites the layout

            Span<byte> payload;
            try
            {
                payload = EncodePropertyValue(propertyType, propertyValue);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (payload.Length != existingLength)
                return false; // Length change rewrites the layout

            if ((ulong)valueOffset + PropertyValueHeader.SIZE + (ulong)payload.Length > (ulong)span.Length)
                return false;
            payload.CopyTo(span.Slice((int)(valueOffset + PropertyValueHeader.SIZE), payload.Length));
            return true;
        }

        // Key not found: adding a property changes the layout
        return false;
    }

    /// <summary>
    /// Encodes a property value into its binary payload form, mirroring
    /// <see cref="Builder.CognitiveGraphBuilder.WritePropertyValue"/>. All
    /// fixed-size numeric types use the machine's native byte order, matching
    /// the builder's <c>AppendStruct</c>-based writes.
    /// </summary>
    private static byte[] EncodePropertyValue(PropertyValueType type, object value) => type switch
    {
        PropertyValueType.String => Encoding.UTF8.GetBytes((string)value),
        PropertyValueType.Int32 => BitConverter.GetBytes((int)value),
        PropertyValueType.UInt32 => BitConverter.GetBytes((uint)value),
        PropertyValueType.Boolean => new[] { (byte)((bool)value ? 1 : 0) },
        PropertyValueType.Double => BitConverter.GetBytes((double)value),
        PropertyValueType.Int64 => BitConverter.GetBytes((long)value),
        PropertyValueType.UInt64 => BitConverter.GetBytes((ulong)value),
        PropertyValueType.Float => BitConverter.GetBytes((float)value),
        PropertyValueType.Binary => (byte[])value,
        _ => throw new ArgumentException($"Unsupported property value type: {type}")
    };

    /// <summary>
    /// Rewrites the serialized interval tree in place after span-changing
    /// patches. The tree is the last buffer section, so its size is fixed by
    /// the node count: the rebuilt serialization must occupy exactly the
    /// space of the old one, which holds because the node set is unchanged.
    /// </summary>
    private static bool TryRewriteIntervalTree(Span<byte> span, uint treeOffset,
        Dictionary<uint, (uint start, uint length)> spanChanges)
    {
        if ((ulong)treeOffset + sizeof(uint) > (ulong)span.Length)
            return false;
        var nodeCount = MemoryMarshal.Read<uint>(span.Slice((int)treeOffset));

        // Serialized layout: [uint count][IntervalNode records...]
        if ((ulong)treeOffset + sizeof(uint) + (ulong)nodeCount * IntervalNode.SIZE > (ulong)span.Length)
            return false;

        var tree = new IntervalTree();
        var recordsOffset = treeOffset + sizeof(uint);
        try
        {
            for (uint i = 0; i < nodeCount; i++)
            {
                var record = span.Slice((int)(recordsOffset + i * IntervalNode.SIZE), IntervalNode.SIZE);
                var start = MemoryMarshal.Read<uint>(record);
                var end = MemoryMarshal.Read<uint>(record.Slice(4));
                var nodeOffset = MemoryMarshal.Read<uint>(record.Slice(8));

                if (spanChanges.TryGetValue(nodeOffset, out var patched))
                {
                    // The builder stores inclusive end offsets: (start, start + length - 1)
                    start = patched.start;
                    end = patched.start + patched.length - 1;
                }

                tree.Add(start, end, nodeOffset);
            }
        }
        catch (ArgumentException)
        {
            return false; // Degenerate span (e.g. zero length): let the rebuild report it
        }

        var serialized = tree.Serialize();
        if (serialized.Length != sizeof(uint) + nodeCount * IntervalNode.SIZE)
            return false; // Same node count must serialize to the same size

        serialized.CopyTo(span.Slice((int)treeOffset, serialized.Length));
        return true;
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
