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
/// Binary layout for the graph header (Schema V2 - Universal Mode).
/// Optimized for massive, polyglot, or whole-system graphs (>4GB).
/// Uses 64-bit offsets and 32-bit IDs.
/// Total size: 64 bytes (8-byte aligned)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct GraphHeaderV2
{
    /// <summary>
    /// File format identifier (0x434F474E for "COGN")
    /// </summary>
    public readonly uint MagicNumber;
    
    /// <summary>
    /// Schema version number (always 2 for V2)
    /// </summary>
    public readonly ushort Version;
    
    /// <summary>
    /// Bit flags for graph properties
    /// </summary>
    public readonly ushort Flags;
    
    /// <summary>
    /// Offset to the root Symbol Node of the parse
    /// </summary>
    public readonly ulong RootNodeOffset;
    
    /// <summary>
    /// Total number of Symbol/Packed/Intermediate nodes
    /// </summary>
    public readonly ulong NodeCount;
    
    /// <summary>
    /// Total number of CPG edges
    /// </summary>
    public readonly ulong EdgeCount;
    
    /// <summary>
    /// Length of the original source text
    /// </summary>
    public readonly ulong SourceTextLength;
    
    /// <summary>
    /// Offset to the start of the source text copy in the buffer
    /// </summary>
    public readonly ulong SourceTextOffset;
    
    /// <summary>
    /// Offset to the interval tree index for spatial queries
    /// </summary>
    public readonly ulong IntervalTreeOffset;

    public GraphHeaderV2(uint magicNumber, ushort version, ushort flags, ulong rootNodeOffset,
        ulong nodeCount, ulong edgeCount, ulong sourceTextLength, ulong sourceTextOffset, ulong intervalTreeOffset = 0)
    {
        MagicNumber = magicNumber;
        Version = version;
        Flags = flags;
        RootNodeOffset = rootNodeOffset;
        NodeCount = nodeCount;
        EdgeCount = edgeCount;
        SourceTextLength = sourceTextLength;
        SourceTextOffset = sourceTextOffset;
        IntervalTreeOffset = intervalTreeOffset;
    }

    /// <summary>
    /// Standard magic number for Cognitive Graph files
    /// </summary>
    public const uint MAGIC_NUMBER = 0x434F474E; // "COGN"
    
    /// <summary>
    /// Schema version for V2
    /// </summary>
    public const ushort SCHEMA_VERSION = 2;
    
    /// <summary>
    /// Size of the header in bytes
    /// </summary>
    public const int SIZE = 64;
}
