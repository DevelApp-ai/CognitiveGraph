using System;
using System.Runtime.InteropServices;
using CognitiveGraph.Core.Buffer;
using CognitiveGraph.Core.Schema;

namespace CognitiveGraph.Core.Accessors;

/// <summary>
/// Zero-allocation accessor for properties
/// </summary>
public readonly ref struct Property
{
    private readonly ReadOnlySpan<byte> _dataSpan;
    private readonly CognitiveGraphBuffer _graph;

    internal Property(ReadOnlySpan<byte> dataSpan, CognitiveGraphBuffer graph)
    {
        if (dataSpan.Length < PropertyData.SIZE)
            throw new ArgumentException("Data span too small for Property");
        
        _dataSpan = dataSpan;
        _graph = graph;
    }

    /// <summary>
    /// Offset to the property key string
    /// </summary>
    public uint KeyOffset => MemoryMarshal.Read<uint>(_dataSpan);

    /// <summary>
    /// Offset to the property value
    /// </summary>
    public uint ValueOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(4));

    /// <summary>
    /// Gets the property key
    /// </summary>
    public string GetKey()
    {
        return _graph.ReadString(KeyOffset);
    }

    /// <summary>
    /// Gets the property value
    /// </summary>
    public PropertyValue GetValue()
    {
        // First read the header to get the actual length
        var headerSpan = _graph.Slice((int)ValueOffset, PropertyValueHeader.SIZE);
        var header = System.Runtime.InteropServices.MemoryMarshal.Read<PropertyValueHeader>(headerSpan);
        
        // Now slice with the correct total size (header + data)
        var totalSize = PropertyValueHeader.SIZE + (int)header.Length;
        var valueSpan = _graph.Slice((int)ValueOffset, totalSize);
        return new PropertyValue(valueSpan, _graph);
    }
}

/// <summary>
/// Zero-allocation collection of properties
/// </summary>
public readonly ref struct PropertyCollection
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;

    internal PropertyCollection(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
    }

    /// <summary>
    /// Number of properties in the collection
    /// </summary>
    public int Count => _data.Length / PropertyData.SIZE;

    /// <summary>
    /// Gets a property by index
    /// </summary>
    public Property this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = index * PropertyData.SIZE;
            var span = _data.Slice(offset, PropertyData.SIZE);
            return new Property(span, _graph);
        }
    }

    /// <summary>
    /// Enumerates all properties
    /// </summary>
    public PropertyEnumerator GetEnumerator() => new(_data, _graph);
}

/// <summary>
/// Enumerator for properties
/// </summary>
public ref struct PropertyEnumerator
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;
    private int _currentIndex;

    internal PropertyEnumerator(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
        _currentIndex = -1;
    }

    public Property Current
    {
        get
        {
            var offset = _currentIndex * PropertyData.SIZE;
            var span = _data.Slice(offset, PropertyData.SIZE);
            return new Property(span, _graph);
        }
    }

    public bool MoveNext()
    {
        _currentIndex++;
        return _currentIndex < _data.Length / PropertyData.SIZE;
    }
}

/// <summary>
/// Zero-allocation accessor for property values
/// </summary>
public readonly ref struct PropertyValue
{
    private readonly ReadOnlySpan<byte> _dataSpan;
    private readonly CognitiveGraphBuffer _graph;

    internal PropertyValue(ReadOnlySpan<byte> dataSpan, CognitiveGraphBuffer graph)
    {
        _dataSpan = dataSpan;
        _graph = graph;
    }

    /// <summary>
    /// Type of the property value
    /// </summary>
    public PropertyValueType Type => (PropertyValueType)MemoryMarshal.Read<ushort>(_dataSpan);

    /// <summary>
    /// Length of the value data
    /// </summary>
    public uint Length => MemoryMarshal.Read<uint>(_dataSpan.Slice(4));

    /// <summary>
    /// Gets the value as a string
    /// </summary>
    public string AsString()
    {
        if (Type != PropertyValueType.String)
            throw new InvalidOperationException($"Property type is {Type}, not String");

        var valueData = _dataSpan.Slice(PropertyValueHeader.SIZE, (int)Length);
        return System.Text.Encoding.UTF8.GetString(valueData);
    }

    /// <summary>
    /// Gets the value as an integer
    /// </summary>
    public int AsInt32()
    {
        if (Type != PropertyValueType.Int32)
            throw new InvalidOperationException($"Property type is {Type}, not Int32");

        return MemoryMarshal.Read<int>(_dataSpan.Slice(PropertyValueHeader.SIZE));
    }

    /// <summary>
    /// Gets the value as an unsigned integer
    /// </summary>
    public uint AsUInt32()
    {
        if (Type != PropertyValueType.UInt32)
            throw new InvalidOperationException($"Property type is {Type}, not UInt32");

        return MemoryMarshal.Read<uint>(_dataSpan.Slice(PropertyValueHeader.SIZE));
    }

    /// <summary>
    /// Gets the value as a boolean
    /// </summary>
    public bool AsBoolean()
    {
        if (Type != PropertyValueType.Boolean)
            throw new InvalidOperationException($"Property type is {Type}, not Boolean");

        return MemoryMarshal.Read<byte>(_dataSpan.Slice(PropertyValueHeader.SIZE)) != 0;
    }

    /// <summary>
    /// Gets the value as a double
    /// </summary>
    public double AsDouble()
    {
        if (Type != PropertyValueType.Double)
            throw new InvalidOperationException($"Property type is {Type}, not Double");

        return MemoryMarshal.Read<double>(_dataSpan.Slice(PropertyValueHeader.SIZE));
    }

    /// <summary>
    /// Gets the raw value data
    /// </summary>
    public ReadOnlySpan<byte> AsBytes()
    {
        return _dataSpan.Slice(PropertyValueHeader.SIZE, (int)Length);
    }
}

/// <summary>
/// Collection of symbol node offsets (for child node lists)
/// </summary>
public readonly ref struct SymbolNodeOffsetCollection
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;

    internal SymbolNodeOffsetCollection(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
    }

    /// <summary>
    /// Number of child nodes
    /// </summary>
    public int Count => _data.Length / sizeof(uint);

    /// <summary>
    /// Gets a child node by index
    /// </summary>
    public SymbolNode this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = MemoryMarshal.Read<uint>(_data.Slice(index * sizeof(uint)));
            var nodeSpan = _graph.Slice((int)offset, SymbolNodeData.SIZE);
            return new SymbolNode(nodeSpan, _graph);
        }
    }

    /// <summary>
    /// Enumerates all child nodes
    /// </summary>
    public SymbolNodeOffsetEnumerator GetEnumerator() => new(_data, _graph);
}

/// <summary>
/// Enumerator for symbol node offsets
/// </summary>
public ref struct SymbolNodeOffsetEnumerator
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;
    private int _currentIndex;

    internal SymbolNodeOffsetEnumerator(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
        _currentIndex = -1;
    }

    public SymbolNode Current
    {
        get
        {
            var offset = MemoryMarshal.Read<uint>(_data.Slice(_currentIndex * sizeof(uint)));
            var nodeSpan = _graph.Slice((int)offset, SymbolNodeData.SIZE);
            return new SymbolNode(nodeSpan, _graph);
        }
    }

    public bool MoveNext()
    {
        _currentIndex++;
        return _currentIndex < _data.Length / sizeof(uint);
    }
}