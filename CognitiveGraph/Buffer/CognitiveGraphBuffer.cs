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
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Buffer;

/// <summary>
/// High-performance, zero-copy buffer for storing the Cognitive Graph.
/// Uses a contiguous memory layout with offset-based navigation.
/// This is an alias for CompactGraphBuffer for backward compatibility.
/// </summary>
[Obsolete("Use CompactGraphBuffer for V1 schema or UniversalGraphBuffer for V2 schema")]
public sealed class CognitiveGraphBuffer : CompactGraphBuffer
{
    /// <summary>
    /// Creates a new buffer with the specified capacity
    /// </summary>
    public CognitiveGraphBuffer(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Creates a buffer view over existing data (for memory-mapped files, etc.)
    /// </summary>
    public CognitiveGraphBuffer(byte[] data, bool takeOwnership = false) : base(data, takeOwnership)
    {
    }

    /// <summary>
    /// Creates a zero-copy buffer view over a memory-mapped file
    /// </summary>
    internal CognitiveGraphBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long length) 
        : base(mmf, accessor, length)
    {
    }
}