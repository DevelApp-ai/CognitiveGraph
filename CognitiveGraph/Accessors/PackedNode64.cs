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
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Accessors;

/// <summary>
/// Zero-allocation accessor for Packed Nodes in Schema V2 (Universal Mode).
/// Uses 64-bit offsets and 32-bit IDs for high-scale graphs.
/// </summary>
public readonly unsafe ref struct PackedNode64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _offset;

    internal PackedNode64(UniversalGraphBuffer buffer, long offset)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _offset = offset;
    }

    /// <summary>
    /// Identifier for the grammar rule applied
    /// </summary>
    public uint RuleID => _buffer.ReadUInt32(_offset);

    /// <summary>
    /// Reserved field (4 bytes)
    /// </summary>
    public uint Reserved => _buffer.ReadUInt32(_offset + 4);

    /// <summary>
    /// Offset to the list of child Symbol/Intermediate Nodes
    /// </summary>
    public ulong ChildNodesOffset => _buffer.ReadUInt64(_offset + 8);

    /// <summary>
    /// Offset to the list of CPG edges for this specific derivation
    /// </summary>
    public ulong CpgEdgesOffset => _buffer.ReadUInt64(_offset + 16);

    /// <summary>
    /// Gets all child nodes for this packed node
    /// </summary>
    public SymbolNodeOffsetCollection64 GetChildNodes()
    {
        if (ChildNodesOffset == 0)
            return new SymbolNodeOffsetCollection64(_buffer, 0, 0);

        var count = _buffer.ReadUInt64((long)ChildNodesOffset);
        return new SymbolNodeOffsetCollection64(_buffer, (long)ChildNodesOffset + 8, count);
    }
}
