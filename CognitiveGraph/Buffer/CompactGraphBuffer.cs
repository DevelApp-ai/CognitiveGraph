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
/// High-performance, zero-copy buffer for Schema V1 (Compact Mode).
/// Uses safe Span-based access with 32-bit offsets. Max file size ~4GB.
/// </summary>
public unsafe class CompactGraphBuffer : IGraphBuffer
{
    private readonly byte[]? _buffer;
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly byte* _pinnedPtr;
    private readonly int _pinnedLength;
    private bool _disposed;

    /// <summary>
    /// Creates a new buffer with the specified capacity
    /// </summary>
    public CompactGraphBuffer(int capacity)
    {
        _buffer = new byte[capacity];
    }

    /// <summary>
    /// Creates a buffer view over existing data (for memory-mapped files, etc.)
    /// </summary>
    public CompactGraphBuffer(byte[] data, bool takeOwnership = false)
    {
        _buffer = data ?? throw new ArgumentNullException(nameof(data));
    }

    /// <summary>
    /// Creates a zero-copy buffer over a memory-mapped file (V1 file loads).
    /// The view pointer is acquired once and released on Dispose; all spans
    /// returned by this buffer point directly into the mapped view.
    /// </summary>
    internal CompactGraphBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long length)
    {
        if (length > int.MaxValue)
            throw new NotSupportedException(
                $"The V1 (Compact) schema supports graphs up to {int.MaxValue} bytes. Use the V2 (Universal) schema for larger graphs.");

        _mmf = mmf ?? throw new ArgumentNullException(nameof(mmf));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        try
        {
            if (ptr == null)
                throw new InvalidOperationException("Failed to acquire pointer to memory-mapped file");

            _pinnedPtr = ptr;
            _pinnedLength = (int)length;
        }
        catch
        {
            if (ptr != null)
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            throw;
        }
    }

    /// <summary>
    /// Returns a read-only span over the backing storage: either the managed
    /// array (in-memory graphs) or the pinned memory-mapped view (file-backed graphs).
    /// </summary>
    private ReadOnlySpan<byte> Data => _buffer != null
        ? new ReadOnlySpan<byte>(_buffer)
        : new ReadOnlySpan<byte>(_pinnedPtr, _pinnedLength);

    /// <summary>
    /// Total number of bytes in the backing storage.
    /// </summary>
    private int BufferLength => _buffer != null ? _buffer.Length : _pinnedLength;

    /// <summary>
    /// Gets the complete buffer as a read-only span
    /// </summary>
    public ReadOnlySpan<byte> AsSpan() => Data;

    /// <summary>
    /// Gets a slice of the buffer starting at the specified offset
    /// </summary>
    public ReadOnlySpan<byte> Slice(int offset) 
    {
        if (offset < 0 || offset >= BufferLength)
            throw new ArgumentOutOfRangeException(nameof(offset));
        
        return Data.Slice(offset);
    }

    /// <summary>
    /// Gets a slice of the buffer with a specific length
    /// </summary>
    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (offset < 0 || offset >= BufferLength)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || offset + length > BufferLength)
            throw new ArgumentOutOfRangeException(nameof(length));
        
        return Data.Slice(offset, length);
    }

    /// <summary>
    /// Gets the total size of the buffer
    /// </summary>
    public int Length => BufferLength;
    
    /// <summary>
    /// Gets the total size of the buffer (IGraphBuffer implementation)
    /// </summary>
    long IGraphBuffer.Length => BufferLength;

    /// <summary>
    /// Validates the buffer has a valid graph header
    /// </summary>
    public bool IsValidGraph()
    {
        try
        {
            if (BufferLength < GraphHeader.SIZE)
                return false;

            var header = MemoryMarshal.Read<GraphHeader>(AsSpan());
            return header.MagicNumber == GraphHeader.MAGIC_NUMBER;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the graph header from the buffer
    /// </summary>
    public GraphHeader GetHeader()
    {
        if (BufferLength < GraphHeader.SIZE)
            throw new InvalidOperationException("Buffer too small for header");

        return MemoryMarshal.Read<GraphHeader>(AsSpan());
    }

    /// <summary>
    /// Reads a value of type T from the buffer at the specified offset
    /// </summary>
    public T Read<T>(uint offset) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        if (offset + size > BufferLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return MemoryMarshal.Read<T>(Slice((int)offset, size));
    }

    /// <summary>
    /// Reads a null-terminated string from the buffer at the specified offset
    /// </summary>
    public string ReadString(uint offset)
    {
        if (offset >= BufferLength)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var span = Slice((int)offset);
        var nullIndex = span.IndexOf((byte)0);
        
        if (nullIndex == -1)
            throw new InvalidOperationException("String is not null-terminated");

        return System.Text.Encoding.UTF8.GetString(span.Slice(0, nullIndex));
    }

    /// <summary>
    /// Reads a list count at the specified offset
    /// </summary>
    public uint ReadListCount(uint offset)
    {
        return Read<uint>(offset);
    }

    /// <summary>
    /// Gets a span for a list of items starting after the count field
    /// </summary>
    public ReadOnlySpan<byte> GetListSpan(uint offset, int itemSize)
    {
        var count = ReadListCount(offset);
        var listStart = offset + sizeof(uint);
        var listSize = (int)(count * itemSize);
        
        return Slice((int)listStart, listSize);
    }

    /// <summary>
    /// Gets the underlying buffer for advanced scenarios (use with caution)
    /// </summary>
    internal byte[] GetInternalBuffer() => _buffer 
        ?? throw new InvalidOperationException("File-backed buffers do not expose a managed internal array.");

    // IGraphBuffer interface implementations
    
    byte IGraphBuffer.ReadByte(long offset) => Slice((int)offset, 1)[0];
    
    short IGraphBuffer.ReadInt16(long offset) => MemoryMarshal.Read<short>(Slice((int)offset, sizeof(short)));
    
    ushort IGraphBuffer.ReadUInt16(long offset) => MemoryMarshal.Read<ushort>(Slice((int)offset, sizeof(ushort)));
    
    int IGraphBuffer.ReadInt32(long offset) => MemoryMarshal.Read<int>(Slice((int)offset, sizeof(int)));
    
    uint IGraphBuffer.ReadUInt32(long offset) => MemoryMarshal.Read<uint>(Slice((int)offset, sizeof(uint)));
    
    long IGraphBuffer.ReadInt64(long offset) => MemoryMarshal.Read<long>(Slice((int)offset, sizeof(long)));
    
    ulong IGraphBuffer.ReadUInt64(long offset) => MemoryMarshal.Read<ulong>(Slice((int)offset, sizeof(ulong)));
    
    ReadOnlySpan<byte> IGraphBuffer.GetSpan(long offset, int length) => Slice((int)offset, length);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_accessor != null)
        {
            if (_pinnedPtr != null)
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _accessor.Dispose();
            _mmf?.Dispose();
        }
    }
}