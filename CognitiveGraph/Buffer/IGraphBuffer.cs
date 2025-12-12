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

namespace CognitiveGraph.Buffer;

/// <summary>
/// Abstraction for graph buffer access to support both V1 (Compact) and V2 (Universal) schemas.
/// V1 uses safe Span-based access, V2 uses unsafe pointer arithmetic.
/// </summary>
internal interface IGraphBuffer : IDisposable
{
    /// <summary>
    /// Gets the total size of the buffer
    /// </summary>
    long Length { get; }
    
    /// <summary>
    /// Reads a byte at the specified offset
    /// </summary>
    byte ReadByte(long offset);
    
    /// <summary>
    /// Reads an Int16 at the specified offset
    /// </summary>
    short ReadInt16(long offset);
    
    /// <summary>
    /// Reads a UInt16 at the specified offset
    /// </summary>
    ushort ReadUInt16(long offset);
    
    /// <summary>
    /// Reads an Int32 at the specified offset
    /// </summary>
    int ReadInt32(long offset);
    
    /// <summary>
    /// Reads a UInt32 at the specified offset
    /// </summary>
    uint ReadUInt32(long offset);
    
    /// <summary>
    /// Reads an Int64 at the specified offset
    /// </summary>
    long ReadInt64(long offset);
    
    /// <summary>
    /// Reads a UInt64 at the specified offset
    /// </summary>
    ulong ReadUInt64(long offset);
    
    /// <summary>
    /// Gets a ReadOnlySpan for the specified range
    /// </summary>
    ReadOnlySpan<byte> GetSpan(long offset, int length);
    
    /// <summary>
    /// Validates the buffer has a valid graph header
    /// </summary>
    bool IsValidGraph();
}
