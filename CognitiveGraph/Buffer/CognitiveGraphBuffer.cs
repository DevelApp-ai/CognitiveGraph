using System;
using System.Runtime.InteropServices;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Buffer;

/// <summary>
/// High-performance, zero-copy buffer for storing the Cognitive Graph.
/// Uses a contiguous memory layout with offset-based navigation.
/// </summary>
public sealed class CognitiveGraphBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly bool _isOwner;
    private bool _disposed;

    /// <summary>
    /// Creates a new buffer with the specified capacity
    /// </summary>
    public CognitiveGraphBuffer(int capacity)
    {
        _buffer = new byte[capacity];
        _isOwner = true;
    }

    /// <summary>
    /// Creates a buffer view over existing data (for memory-mapped files, etc.)
    /// </summary>
    public CognitiveGraphBuffer(byte[] data, bool takeOwnership = false)
    {
        _buffer = data ?? throw new ArgumentNullException(nameof(data));
        _isOwner = takeOwnership;
    }

    /// <summary>
    /// Gets the complete buffer as a read-only span
    /// </summary>
    public ReadOnlySpan<byte> AsSpan() => new(_buffer);

    /// <summary>
    /// Gets a slice of the buffer starting at the specified offset
    /// </summary>
    public ReadOnlySpan<byte> Slice(int offset) 
    {
        if (offset < 0 || offset >= _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        
        return new ReadOnlySpan<byte>(_buffer, offset, _buffer.Length - offset);
    }

    /// <summary>
    /// Gets a slice of the buffer with a specific length
    /// </summary>
    public ReadOnlySpan<byte> Slice(int offset, int length)
    {
        if (offset < 0 || offset >= _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0 || offset + length > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(length));
        
        return new ReadOnlySpan<byte>(_buffer, offset, length);
    }

    /// <summary>
    /// Gets the total size of the buffer
    /// </summary>
    public int Length => _buffer.Length;

    /// <summary>
    /// Validates the buffer has a valid graph header
    /// </summary>
    public bool IsValidGraph()
    {
        try
        {
            if (_buffer.Length < GraphHeader.SIZE)
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
        if (_buffer.Length < GraphHeader.SIZE)
            throw new InvalidOperationException("Buffer too small for header");

        return MemoryMarshal.Read<GraphHeader>(AsSpan());
    }

    /// <summary>
    /// Reads a value of type T from the buffer at the specified offset
    /// </summary>
    public T Read<T>(uint offset) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        if (offset + size > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        return MemoryMarshal.Read<T>(Slice((int)offset, size));
    }

    /// <summary>
    /// Reads a null-terminated string from the buffer at the specified offset
    /// </summary>
    public string ReadString(uint offset)
    {
        if (offset >= _buffer.Length)
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
    internal byte[] GetInternalBuffer() => _buffer;

    public void Dispose()
    {
        if (!_disposed && _isOwner)
        {
            // In a real implementation, we might need to handle memory-mapped files here
            _disposed = true;
        }
    }
}