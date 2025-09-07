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
using System.Runtime.InteropServices;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Accessors;

/// <summary>
/// Zero-allocation accessor for Symbol Nodes in the graph.
/// Uses readonly ref struct to ensure stack-only allocation.
/// </summary>
public readonly ref struct SymbolNode
{
    private readonly ReadOnlySpan<byte> _dataSpan;
    private readonly CognitiveGraphBuffer _graph;

    internal SymbolNode(ReadOnlySpan<byte> dataSpan, CognitiveGraphBuffer graph)
    {
        if (dataSpan.Length < SymbolNodeData.SIZE)
            throw new ArgumentException("Data span too small for SymbolNode");
        
        _dataSpan = dataSpan;
        _graph = graph;
    }

    /// <summary>
    /// Identifier for the grammar symbol (terminal/non-terminal)
    /// </summary>
    public ushort SymbolID => MemoryMarshal.Read<ushort>(_dataSpan);

    /// <summary>
    /// High-level semantic type (e.g., FunctionDeclaration)
    /// </summary>
    public ushort NodeType => MemoryMarshal.Read<ushort>(_dataSpan.Slice(2));

    /// <summary>
    /// Start character index in the source text
    /// </summary>
    public uint SourceStart => MemoryMarshal.Read<uint>(_dataSpan.Slice(4));

    /// <summary>
    /// Length of the source text span for this node
    /// </summary>
    public uint SourceLength => MemoryMarshal.Read<uint>(_dataSpan.Slice(8));

    /// <summary>
    /// End position in the source text (start + length)
    /// </summary>
    public uint SourceEnd => SourceStart + SourceLength;

    /// <summary>
    /// Offset to the list of child Packed Nodes
    /// </summary>
    public uint PackedNodesOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(12));

    /// <summary>
    /// Offset to the list of key-value properties for this node
    /// </summary>
    public uint PropertiesOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(16));

    /// <summary>
    /// Gets the source text for this node
    /// </summary>
    public ReadOnlySpan<char> GetSourceText()
    {
        var header = _graph.GetHeader();
        var sourceBytes = _graph.Slice((int)(header.SourceTextOffset + SourceStart), (int)SourceLength);
        
        // Convert UTF-8 bytes to chars (simplified - in real implementation might need proper UTF-8 handling)
        return System.Text.Encoding.UTF8.GetString(sourceBytes).AsSpan();
    }

    /// <summary>
    /// Gets all packed nodes (derivations) for this symbol
    /// </summary>
    public PackedNodeOffsetCollection GetPackedNodes()
    {
        if (PackedNodesOffset == 0)
            return new PackedNodeOffsetCollection(ReadOnlySpan<byte>.Empty, _graph);

        var listSpan = _graph.GetListSpan(PackedNodesOffset, sizeof(uint));
        return new PackedNodeOffsetCollection(listSpan, _graph);
    }

    /// <summary>
    /// Gets all properties for this node
    /// </summary>
    public PropertyCollection GetProperties()
    {
        if (PropertiesOffset == 0)
            return new PropertyCollection(ReadOnlySpan<byte>.Empty, _graph);

        var listSpan = _graph.GetListSpan(PropertiesOffset, PropertyData.SIZE);
        return new PropertyCollection(listSpan, _graph);
    }

    /// <summary>
    /// Checks if this node is ambiguous (has multiple packed nodes)
    /// </summary>
    public bool IsAmbiguous => PackedNodesOffset != 0 && _graph.ReadListCount(PackedNodesOffset) > 1;

    /// <summary>
    /// Gets a property value by key
    /// </summary>
    public bool TryGetProperty(string key, out PropertyValue value)
    {
        var properties = GetProperties();
        foreach (var prop in properties)
        {
            if (prop.GetKey() == key)
            {
                value = prop.GetValue();
                return true;
            }
        }
        value = default;
        return false;
    }
}