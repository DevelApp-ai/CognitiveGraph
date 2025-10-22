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

using System.Collections.Generic;
using CognitiveGraph.Schema;

namespace CognitiveGraph;

/// <summary>
/// Represents an edit operation that can be applied to a cognitive graph
/// </summary>
public abstract class EditOperation
{
    /// <summary>
    /// Target node offset for this operation
    /// </summary>
    public uint TargetOffset { get; protected set; }

    protected EditOperation(uint targetOffset)
    {
        TargetOffset = targetOffset;
    }
}

/// <summary>
/// Inserts a new node into the graph
/// </summary>
public class InsertNodeOperation : EditOperation
{
    public ushort SymbolId { get; }
    public ushort NodeType { get; }
    public uint SourceStart { get; }
    public uint SourceLength { get; }
    public IReadOnlyList<uint>? PackedNodeOffsets { get; }
    public IReadOnlyList<(string key, PropertyValueType type, object value)>? Properties { get; }

    public InsertNodeOperation(uint targetOffset, ushort symbolId, ushort nodeType, 
        uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null,
        IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
        : base(targetOffset)
    {
        SymbolId = symbolId;
        NodeType = nodeType;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        PackedNodeOffsets = packedNodeOffsets;
        Properties = properties;
    }
}

/// <summary>
/// Replaces an existing node with new data
/// </summary>
public class ReplaceNodeOperation : EditOperation
{
    public ushort NewSymbolId { get; }
    public ushort NewNodeType { get; }
    public uint NewSourceStart { get; }
    public uint NewSourceLength { get; }
    public IReadOnlyList<uint>? NewPackedNodeOffsets { get; }
    public IReadOnlyList<(string key, PropertyValueType type, object value)>? NewProperties { get; }

    public ReplaceNodeOperation(uint targetOffset, ushort newSymbolId, ushort newNodeType,
        uint newSourceStart, uint newSourceLength,
        IReadOnlyList<uint>? newPackedNodeOffsets = null,
        IReadOnlyList<(string key, PropertyValueType type, object value)>? newProperties = null)
        : base(targetOffset)
    {
        NewSymbolId = newSymbolId;
        NewNodeType = newNodeType;
        NewSourceStart = newSourceStart;
        NewSourceLength = newSourceLength;
        NewPackedNodeOffsets = newPackedNodeOffsets;
        NewProperties = newProperties;
    }
}

/// <summary>
/// Deletes a node from the graph
/// </summary>
public class DeleteNodeOperation : EditOperation
{
    public DeleteNodeOperation(uint targetOffset) : base(targetOffset)
    {
    }
}

/// <summary>
/// Moves a node to a new position in the source
/// </summary>
public class MoveNodeOperation : EditOperation
{
    public uint NewSourceStart { get; }
    public uint NewSourceLength { get; }

    public MoveNodeOperation(uint targetOffset, uint newSourceStart, uint newSourceLength)
        : base(targetOffset)
    {
        NewSourceStart = newSourceStart;
        NewSourceLength = newSourceLength;
    }
}

/// <summary>
/// Updates properties of an existing node
/// </summary>
public class UpdatePropertyOperation : EditOperation
{
    public string PropertyKey { get; }
    public PropertyValueType PropertyType { get; }
    public object PropertyValue { get; }
    public bool RemoveProperty { get; }

    public UpdatePropertyOperation(uint targetOffset, string propertyKey, PropertyValueType propertyType, object propertyValue)
        : base(targetOffset)
    {
        PropertyKey = propertyKey;
        PropertyType = propertyType;
        PropertyValue = propertyValue;
        RemoveProperty = false;
    }

    public UpdatePropertyOperation(uint targetOffset, string propertyKey)
        : base(targetOffset)
    {
        PropertyKey = propertyKey;
        PropertyType = PropertyValueType.String;
        PropertyValue = string.Empty;
        RemoveProperty = true;
    }
}