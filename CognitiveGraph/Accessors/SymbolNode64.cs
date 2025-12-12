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
/// Zero-allocation accessor for Symbol Nodes in Schema V2 (Universal Mode).
/// Uses 64-bit offsets and 32-bit IDs for high-scale graphs.
/// </summary>
public readonly unsafe ref struct SymbolNode64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _offset;

    internal SymbolNode64(UniversalGraphBuffer buffer, long offset)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _offset = offset;
    }

    /// <summary>
    /// Identifier for the grammar symbol (terminal/non-terminal)
    /// </summary>
    public uint SymbolID => _buffer.ReadUInt32(_offset);

    /// <summary>
    /// High-level semantic type (e.g., FunctionDeclaration)
    /// </summary>
    public uint NodeType => _buffer.ReadUInt32(_offset + 4);

    /// <summary>
    /// Start character index in the source text
    /// </summary>
    public uint SourceStart => _buffer.ReadUInt32(_offset + 8);

    /// <summary>
    /// Length of the source text span for this node
    /// </summary>
    public uint SourceLength => _buffer.ReadUInt32(_offset + 12);

    /// <summary>
    /// End position in the source text (start + length)
    /// </summary>
    public uint SourceEnd => SourceStart + SourceLength;

    /// <summary>
    /// Offset to the list of child Packed Nodes
    /// </summary>
    public ulong PackedNodesOffset => _buffer.ReadUInt64(_offset + 16);

    /// <summary>
    /// Offset to the list of key-value properties for this node
    /// </summary>
    public ulong PropertiesOffset => _buffer.ReadUInt64(_offset + 24);

    /// <summary>
    /// Gets the source text for this node
    /// </summary>
    public ReadOnlySpan<char> GetSourceText()
    {
        var header = _buffer.GetHeaderV2();
        var sourceBytes = _buffer.GetSpan((long)(header.SourceTextOffset + SourceStart), (int)SourceLength);
        
        return System.Text.Encoding.UTF8.GetString(sourceBytes).AsSpan();
    }

    /// <summary>
    /// Gets all packed nodes (derivations) for this symbol
    /// </summary>
    public PackedNodeOffsetCollection64 GetPackedNodes()
    {
        if (PackedNodesOffset == 0)
            return new PackedNodeOffsetCollection64(_buffer, 0, 0);

        var count = _buffer.ReadUInt64((long)PackedNodesOffset);
        return new PackedNodeOffsetCollection64(_buffer, (long)PackedNodesOffset + 8, count);
    }

    /// <summary>
    /// Checks if this node is ambiguous (has multiple packed nodes)
    /// </summary>
    public bool IsAmbiguous
    {
        get
        {
            if (PackedNodesOffset == 0)
                return false;
            
            var count = _buffer.ReadUInt64((long)PackedNodesOffset);
            return count > 1;
        }
    }
}

/// <summary>
/// Zero-allocation collection of packed node offsets for V2
/// </summary>
public readonly ref struct PackedNodeOffsetCollection64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _offset;
    private readonly ulong _count;

    internal PackedNodeOffsetCollection64(UniversalGraphBuffer buffer, long offset, ulong count)
    {
        _buffer = buffer;
        _offset = offset;
        _count = count;
    }

    /// <summary>
    /// Number of packed nodes in the collection
    /// </summary>
    public int Count
    {
        get
        {
            if (_count > int.MaxValue)
                throw new InvalidOperationException($"Collection has {_count} items which exceeds int.MaxValue. Use a different access pattern for such large collections.");
            return (int)_count;
        }
    }
    
    /// <summary>
    /// Gets the full count as ulong for very large collections
    /// </summary>
    public ulong LongCount => _count;

    /// <summary>
    /// Gets a packed node at the specified index
    /// </summary>
    public PackedNode64 this[int index]
    {
        get
        {
            if (index < 0 || (ulong)index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = _buffer.ReadUInt64(_offset + (index * sizeof(ulong)));
            return new PackedNode64(_buffer, (long)offset);
        }
    }

    /// <summary>
    /// Enumerator for the collection
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    public ref struct Enumerator
    {
        private readonly PackedNodeOffsetCollection64 _collection;
        private int _index;

        internal Enumerator(PackedNodeOffsetCollection64 collection)
        {
            _collection = collection;
            _index = -1;
        }

        public PackedNode64 Current => _collection[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _collection.Count;
        }
    }
}

/// <summary>
/// Zero-allocation collection of symbol node offsets for V2
/// </summary>
public readonly ref struct SymbolNodeOffsetCollection64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _offset;
    private readonly ulong _count;

    internal SymbolNodeOffsetCollection64(UniversalGraphBuffer buffer, long offset, ulong count)
    {
        _buffer = buffer;
        _offset = offset;
        _count = count;
    }

    /// <summary>
    /// Number of symbol nodes in the collection
    /// </summary>
    public int Count
    {
        get
        {
            if (_count > int.MaxValue)
                throw new InvalidOperationException($"Collection has {_count} items which exceeds int.MaxValue. Use a different access pattern for such large collections.");
            return (int)_count;
        }
    }
    
    /// <summary>
    /// Gets the full count as ulong for very large collections
    /// </summary>
    public ulong LongCount => _count;

    /// <summary>
    /// Gets a symbol node at the specified index
    /// </summary>
    public SymbolNode64 this[int index]
    {
        get
        {
            if (index < 0 || (ulong)index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = _buffer.ReadUInt64(_offset + (index * sizeof(ulong)));
            return new SymbolNode64(_buffer, (long)offset);
        }
    }

    /// <summary>
    /// Enumerator for the collection
    /// </summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    public ref struct Enumerator
    {
        private readonly SymbolNodeOffsetCollection64 _collection;
        private int _index;

        internal Enumerator(SymbolNodeOffsetCollection64 collection)
        {
            _collection = collection;
            _index = -1;
        }

        public SymbolNode64 Current => _collection[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _collection.Count;
        }
    }
}
