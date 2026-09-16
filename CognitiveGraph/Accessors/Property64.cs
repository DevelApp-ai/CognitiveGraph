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
/// Zero-allocation accessor for properties in Schema V2 (Universal Mode).
/// Reads PropertyDataV2 records with full 64-bit key/value offsets.
/// </summary>
public readonly ref struct Property64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _dataOffset;

    internal Property64(UniversalGraphBuffer buffer, long dataOffset)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _dataOffset = dataOffset;
    }

    /// <summary>
    /// Offset to the property key string
    /// </summary>
    public ulong KeyOffset => _buffer.ReadUInt64(_dataOffset);

    /// <summary>
    /// Offset to the property value
    /// </summary>
    public ulong ValueOffset => _buffer.ReadUInt64(_dataOffset + 8);

    /// <summary>
    /// Gets the property key
    /// </summary>
    public string GetKey() => _buffer.ReadString((long)KeyOffset);

    /// <summary>
    /// Gets the property value
    /// </summary>
    public PropertyValue64 GetValue() => new(_buffer, (long)ValueOffset);
}

/// <summary>
/// Zero-allocation collection of properties for Schema V2.
/// Backed by a V2 property list: [64-bit count][PropertyDataV2]...
/// </summary>
public readonly ref struct PropertyCollection64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _itemsOffset;
    private readonly ulong _count;

    internal PropertyCollection64(UniversalGraphBuffer buffer, long listOffset)
    {
        _buffer = buffer;

        if (listOffset == 0)
        {
            _itemsOffset = 0;
            _count = 0;
            return;
        }

        _count = buffer.ReadUInt64(listOffset);
        _itemsOffset = listOffset + sizeof(ulong);
    }

    /// <summary>
    /// Number of properties in the collection
    /// </summary>
    public int Count
    {
        get
        {
            if (_count > int.MaxValue)
                throw new InvalidOperationException($"Collection has {_count} items which exceeds int.MaxValue. Use LongCount.");
            return (int)_count;
        }
    }

    /// <summary>
    /// Gets the full count as ulong for very large collections
    /// </summary>
    public ulong LongCount => _count;

    /// <summary>
    /// Gets a property by index
    /// </summary>
    public Property64 this[int index]
    {
        get
        {
            if (index < 0 || (ulong)index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new Property64(_buffer, _itemsOffset + (index * PropertyDataV2.SIZE));
        }
    }

    /// <summary>
    /// Enumerates all properties
    /// </summary>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>
    /// Enumerator for V2 properties
    /// </summary>
    public ref struct Enumerator
    {
        private readonly PropertyCollection64 _collection;
        private int _index;

        internal Enumerator(PropertyCollection64 collection)
        {
            _collection = collection;
            _index = -1;
        }

        public Property64 Current => _collection[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _collection.Count;
        }
    }
}

/// <summary>
/// Zero-allocation accessor for property values in Schema V2 (Universal Mode).
/// The value layout (PropertyValueHeader + data) is identical to V1.
/// </summary>
public readonly ref struct PropertyValue64
{
    private readonly UniversalGraphBuffer _buffer;
    private readonly long _offset;

    internal PropertyValue64(UniversalGraphBuffer buffer, long offset)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _offset = offset;
    }

    /// <summary>
    /// Type of the property value
    /// </summary>
    public PropertyValueType Type => (PropertyValueType)_buffer.ReadUInt16(_offset);

    /// <summary>
    /// Length of the value data
    /// </summary>
    public uint Length => _buffer.ReadUInt32(_offset + 4);

    private long DataOffset => _offset + PropertyValueHeader.SIZE;

    /// <summary>
    /// Gets the value as a string
    /// </summary>
    public string AsString()
    {
        if (Type != PropertyValueType.String)
            throw new InvalidOperationException($"Property type is {Type}, not String");

        return System.Text.Encoding.UTF8.GetString(_buffer.GetSpan(DataOffset, (int)Length));
    }

    /// <summary>
    /// Gets the value as an integer
    /// </summary>
    public int AsInt32()
    {
        if (Type != PropertyValueType.Int32)
            throw new InvalidOperationException($"Property type is {Type}, not Int32");

        return _buffer.ReadInt32(DataOffset);
    }

    /// <summary>
    /// Gets the value as an unsigned integer
    /// </summary>
    public uint AsUInt32()
    {
        if (Type != PropertyValueType.UInt32)
            throw new InvalidOperationException($"Property type is {Type}, not UInt32");

        return _buffer.ReadUInt32(DataOffset);
    }

    /// <summary>
    /// Gets the value as a boolean
    /// </summary>
    public bool AsBoolean()
    {
        if (Type != PropertyValueType.Boolean)
            throw new InvalidOperationException($"Property type is {Type}, not Boolean");

        return _buffer.ReadByte(DataOffset) != 0;
    }

    /// <summary>
    /// Gets the value as a double
    /// </summary>
    public double AsDouble()
    {
        if (Type != PropertyValueType.Double)
            throw new InvalidOperationException($"Property type is {Type}, not Double");

        return _buffer.ReadStruct<double>(DataOffset);
    }

    /// <summary>
    /// Gets the raw value data
    /// </summary>
    public ReadOnlySpan<byte> AsBytes() => _buffer.GetSpan(DataOffset, (int)Length);
}
