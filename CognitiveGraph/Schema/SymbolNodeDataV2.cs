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


using System.Runtime.InteropServices;

namespace CognitiveGraph.Schema;

/// <summary>
/// Binary layout for a Symbol Node in Schema V2 (Universal Mode).
/// Uses 64-bit offsets and 32-bit IDs for high-scale graphs.
/// Total size: 32 bytes (8-byte aligned)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct SymbolNodeDataV2
{
    /// <summary>
    /// Identifier for the grammar symbol (terminal/non-terminal)
    /// </summary>
    public readonly uint SymbolID;
    
    /// <summary>
    /// High-level semantic type (e.g., FunctionDeclaration)
    /// </summary>
    public readonly uint NodeType;
    
    /// <summary>
    /// Start character index in the source text
    /// </summary>
    public readonly uint SourceStart;
    
    /// <summary>
    /// Length of the source text span for this node
    /// </summary>
    public readonly uint SourceLength;
    
    /// <summary>
    /// Offset to the list of child Packed Nodes
    /// </summary>
    public readonly ulong PackedNodesOffset;
    
    /// <summary>
    /// Offset to the list of key-value properties for this node
    /// </summary>
    public readonly ulong PropertiesOffset;

    public SymbolNodeDataV2(uint symbolId, uint nodeType, uint sourceStart, uint sourceLength,
        ulong packedNodesOffset, ulong propertiesOffset)
    {
        SymbolID = symbolId;
        NodeType = nodeType;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
        PackedNodesOffset = packedNodesOffset;
        PropertiesOffset = propertiesOffset;
    }

    /// <summary>
    /// Size of the SymbolNodeV2 data in bytes
    /// </summary>
    public const int SIZE = 32;
}
