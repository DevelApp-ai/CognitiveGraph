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
using System.Linq;
using CognitiveGraph.Accessors;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph;

/// <summary>
/// High-performance editor for creating modified versions of existing CognitiveGraphs.
/// Uses a queue-and-rebuild pattern for efficient batch operations.
/// </summary>
public sealed class CognitiveGraphEditor : IDisposable
{
    private readonly CognitiveGraph _sourceGraph;
    private readonly List<EditOperation> _operations;
    private readonly Dictionary<uint, uint> _offsetMapping; // Old offset -> New offset
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
        if (_operations.Count == 0)
        {
            // No operations, return a copy of the original
            return CloneOriginalGraph();
        }

        using var builder = new CognitiveGraphBuilder();
        var operationsByOffset = _operations.GroupBy(op => op.TargetOffset).ToDictionary(g => g.Key, g => g.ToList());

        // Rebuild the graph, applying operations as we encounter the target nodes
        var newRootOffset = TraverseAndRebuild(builder, _sourceGraph.Header.RootNodeOffset, operationsByOffset);

        // Apply any insert operations that don't target existing nodes
        foreach (var insertOp in _operations.OfType<InsertNodeOperation>().Where(op => !operationsByOffset.ContainsKey(op.TargetOffset)))
        {
            var newOffset = builder.WriteSymbolNode(insertOp.SymbolId, insertOp.NodeType, insertOp.SourceStart, insertOp.SourceLength, insertOp.PackedNodeOffsets, insertOp.Properties);
            _offsetMapping[insertOp.TargetOffset] = newOffset;
        }

        return new CognitiveGraph(builder.Build(newRootOffset, _sourceGraph.GetSourceText()));
    }

    /// <summary>
    /// Recursively traverses the source graph and rebuilds it with applied operations
    /// </summary>
    private uint TraverseAndRebuild(CognitiveGraphBuilder builder, uint nodeOffset, Dictionary<uint, List<EditOperation>> operationsByOffset)
    {
        var sourceNode = _sourceGraph.GetNodeAt(nodeOffset);

        // Check if this node has any operations
        if (operationsByOffset.TryGetValue(nodeOffset, out var operations))
        {
            foreach (var operation in operations)
            {
                switch (operation)
                {
                    case DeleteNodeOperation:
                        // Skip this node entirely
                        return 0; // Invalid offset indicates deletion

                    case ReplaceNodeOperation replaceOp:
                        // Replace with new node data
                        var newOffset = builder.WriteSymbolNode(
                            replaceOp.NewSymbolId,
                            replaceOp.NewNodeType,
                            replaceOp.NewSourceStart,
                            replaceOp.NewSourceLength,
                            replaceOp.NewPackedNodeOffsets,
                            replaceOp.NewProperties
                        );
                        _offsetMapping[nodeOffset] = newOffset;
                        return newOffset;

                    case MoveNodeOperation moveOp:
                        // Update source position but keep other data
                        var currentProperties = ExtractNodeProperties(sourceNode);
                        var movedOffset = builder.WriteSymbolNode(
                            sourceNode.SymbolID,
                            sourceNode.NodeType,
                            moveOp.NewSourceStart,
                            moveOp.NewSourceLength,
                            null, // TODO: Handle packed nodes
                            currentProperties
                        );
                        _offsetMapping[nodeOffset] = movedOffset;
                        return movedOffset;

                    case UpdatePropertyOperation updateProp:
                        // Update properties while keeping other data
                        var updatedProperties = ExtractNodeProperties(sourceNode);
                        
                        if (updateProp.RemoveProperty)
                        {
                            updatedProperties.RemoveAll(p => p.key == updateProp.PropertyKey);
                        }
                        else
                        {
                            // Remove existing property with same key and add updated one
                            updatedProperties.RemoveAll(p => p.key == updateProp.PropertyKey);
                            updatedProperties.Add((updateProp.PropertyKey, updateProp.PropertyType, updateProp.PropertyValue));
                        }

                        var updatedOffset = builder.WriteSymbolNode(
                            sourceNode.SymbolID,
                            sourceNode.NodeType,
                            sourceNode.SourceStart,
                            sourceNode.SourceLength,
                            null, // TODO: Handle packed nodes
                            updatedProperties
                        );
                        _offsetMapping[nodeOffset] = updatedOffset;
                        return updatedOffset;
                }
            }
        }

        // No operations for this node, copy it as-is
        var properties = ExtractNodeProperties(sourceNode);
        var copiedOffset = builder.WriteSymbolNode(
            sourceNode.SymbolID,
            sourceNode.NodeType,
            sourceNode.SourceStart,
            sourceNode.SourceLength,
            null, // TODO: Handle packed nodes and child traversal
            properties
        );
        
        _offsetMapping[nodeOffset] = copiedOffset;
        return copiedOffset;
    }

    /// <summary>
    /// Extracts all properties from a node into a list
    /// </summary>
    private List<(string key, PropertyValueType type, object value)> ExtractNodeProperties(SymbolNode node)
    {
        var properties = new List<(string key, PropertyValueType type, object value)>();
        
        // Note: This is a simplified implementation. In a full implementation,
        // we would need to enumerate all properties from the node.
        // For now, we'll return an empty list as property enumeration
        // would require extending the SymbolNode API.
        
        return properties;
    }

    /// <summary>
    /// Creates a copy of the original graph when no operations are queued
    /// </summary>
    private CognitiveGraph CloneOriginalGraph()
    {
        // For now, return the original graph. In a full implementation,
        // we would create a proper copy.
        return _sourceGraph;
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
            _disposed = true;
        }
    }
}