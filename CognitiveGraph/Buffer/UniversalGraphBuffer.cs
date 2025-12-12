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
/// High-performance buffer for Schema V2 (Universal Mode).
/// Uses unsafe pointer arithmetic to bypass the .NET 2GB array limit.
/// Supports files up to 16 Exabytes with 64-bit offsets.
/// </summary>
public sealed unsafe class UniversalGraphBuffer : IGraphBuffer
{
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly byte* _ptr;
    private readonly long _length;
    private bool _disposed;

    /// <summary>
    /// Creates a Universal buffer from a memory-mapped file
    /// </summary>
    public UniversalGraphBuffer(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!System.IO.File.Exists(filePath))
            throw new System.IO.FileNotFoundException($"Graph file not found: {filePath}");

        try
        {
            _mmf = MemoryMappedFile.CreateFromFile(filePath, System.IO.FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            _length = new System.IO.FileInfo(filePath).Length;

            byte* ptr = null;
            _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
            _ptr = ptr;

            if (_ptr == null)
                throw new InvalidOperationException("Failed to acquire pointer to memory-mapped file");
        }
        catch
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a Universal buffer from an existing memory-mapped file and accessor
    /// </summary>
    internal UniversalGraphBuffer(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor, long length)
    {
        _mmf = mmf ?? throw new ArgumentNullException(nameof(mmf));
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _length = length;

        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _ptr = ptr;

        if (_ptr == null)
            throw new InvalidOperationException("Failed to acquire pointer to memory-mapped file");
    }

    /// <summary>
    /// Gets the total size of the buffer
    /// </summary>
    public long Length => _length;

    /// <summary>
    /// Validates the buffer has a valid graph header
    /// </summary>
    public bool IsValidGraph()
    {
        try
        {
            if (_length < GraphHeaderPreamble.SIZE)
                return false;

            var preamble = ReadStruct<GraphHeaderPreamble>(0);
            return preamble.MagicNumber == GraphHeaderPreamble.MAGIC_NUMBER &&
                   (preamble.Version == 1 || preamble.Version == 2);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a byte at the specified offset
    /// </summary>
    public byte ReadByte(long offset)
    {
        ValidateOffset(offset, sizeof(byte));
        return *(_ptr + offset);
    }

    /// <summary>
    /// Reads an Int16 at the specified offset
    /// </summary>
    public short ReadInt16(long offset)
    {
        ValidateOffset(offset, sizeof(short));
        return *(short*)(_ptr + offset);
    }

    /// <summary>
    /// Reads a UInt16 at the specified offset
    /// </summary>
    public ushort ReadUInt16(long offset)
    {
        ValidateOffset(offset, sizeof(ushort));
        return *(ushort*)(_ptr + offset);
    }

    /// <summary>
    /// Reads an Int32 at the specified offset
    /// </summary>
    public int ReadInt32(long offset)
    {
        ValidateOffset(offset, sizeof(int));
        return *(int*)(_ptr + offset);
    }

    /// <summary>
    /// Reads a UInt32 at the specified offset
    /// </summary>
    public uint ReadUInt32(long offset)
    {
        ValidateOffset(offset, sizeof(uint));
        return *(uint*)(_ptr + offset);
    }

    /// <summary>
    /// Reads an Int64 at the specified offset
    /// </summary>
    public long ReadInt64(long offset)
    {
        ValidateOffset(offset, sizeof(long));
        return *(long*)(_ptr + offset);
    }

    /// <summary>
    /// Reads a UInt64 at the specified offset
    /// </summary>
    public ulong ReadUInt64(long offset)
    {
        ValidateOffset(offset, sizeof(ulong));
        return *(ulong*)(_ptr + offset);
    }

    /// <summary>
    /// Gets a ReadOnlySpan for the specified range
    /// </summary>
    public ReadOnlySpan<byte> GetSpan(long offset, int length)
    {
        ValidateOffset(offset, length);
        return new ReadOnlySpan<byte>(_ptr + offset, length);
    }

    /// <summary>
    /// Reads a structure from the buffer at the specified offset
    /// </summary>
    public T ReadStruct<T>(long offset) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        ValidateOffset(offset, size);
        return *(T*)(_ptr + offset);
    }

    /// <summary>
    /// Gets the V2 header from the buffer
    /// </summary>
    public GraphHeaderV2 GetHeaderV2()
    {
        if (_length < GraphHeaderV2.SIZE)
            throw new InvalidOperationException("Buffer too small for V2 header");

        return ReadStruct<GraphHeaderV2>(0);
    }

    /// <summary>
    /// Reads a null-terminated string from the buffer at the specified offset
    /// </summary>
    public string ReadString(ulong offset)
    {
        if ((long)offset >= _length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var start = _ptr + (long)offset;
        var current = start;
        var maxLength = _length - (long)offset;

        // Find null terminator
        long length = 0;
        while (length < maxLength && *current != 0)
        {
            current++;
            length++;
        }

        if (length >= maxLength)
            throw new InvalidOperationException("String is not null-terminated");

        return System.Text.Encoding.UTF8.GetString(start, (int)length);
    }

    private void ValidateOffset(long offset, long size)
    {
        if (offset < 0 || offset + size > _length)
            throw new ArgumentOutOfRangeException(nameof(offset));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_accessor != null)
            {
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
                _accessor.Dispose();
            }
            _mmf?.Dispose();
            _disposed = true;
        }
    }
}
