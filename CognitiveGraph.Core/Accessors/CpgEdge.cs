using System;
using System.Runtime.InteropServices;
using CognitiveGraph.Core.Buffer;
using CognitiveGraph.Core.Schema;

namespace CognitiveGraph.Core.Accessors;

/// <summary>
/// Zero-allocation accessor for CPG edges
/// </summary>
public readonly ref struct CpgEdge
{
    private readonly ReadOnlySpan<byte> _dataSpan;
    private readonly CognitiveGraphBuffer _graph;

    internal CpgEdge(ReadOnlySpan<byte> dataSpan, CognitiveGraphBuffer graph)
    {
        if (dataSpan.Length < CpgEdgeData.SIZE)
            throw new ArgumentException("Data span too small for CpgEdge");
        
        _dataSpan = dataSpan;
        _graph = graph;
    }

    /// <summary>
    /// Type of edge (AST_CHILD, CONTROL_FLOW, DATA_FLOW, etc.)
    /// </summary>
    public EdgeType EdgeType => (EdgeType)MemoryMarshal.Read<ushort>(_dataSpan);

    /// <summary>
    /// Offset to the target Symbol Node of the edge
    /// </summary>
    public uint TargetNodeOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(4));

    /// <summary>
    /// Offset to the list of properties for this edge
    /// </summary>
    public uint PropertiesOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(8));

    /// <summary>
    /// Gets the target node of this edge
    /// </summary>
    public SymbolNode GetTargetNode()
    {
        var targetSpan = _graph.Slice((int)TargetNodeOffset, SymbolNodeData.SIZE);
        return new SymbolNode(targetSpan, _graph);
    }

    /// <summary>
    /// Gets all properties for this edge
    /// </summary>
    public PropertyCollection GetProperties()
    {
        if (PropertiesOffset == 0)
            return new PropertyCollection(ReadOnlySpan<byte>.Empty, _graph);

        var listSpan = _graph.GetListSpan(PropertiesOffset, PropertyData.SIZE);
        return new PropertyCollection(listSpan, _graph);
    }

    /// <summary>
    /// Gets a property value by key
    /// </summary>
    public bool TryGetProperty(string key, out PropertyValue value)
    {
        var properties = GetProperties();
        foreach (var prop in properties)
        {
            if (prop.GetKey() == key)
            {
                value = prop.GetValue();
                return true;
            }
        }
        value = default;
        return false;
    }
}

/// <summary>
/// Zero-allocation collection of CPG edges
/// </summary>
public readonly ref struct CpgEdgeCollection
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;

    internal CpgEdgeCollection(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
    }

    /// <summary>
    /// Number of edges in the collection
    /// </summary>
    public int Count => _data.Length / CpgEdgeData.SIZE;

    /// <summary>
    /// Gets an edge by index
    /// </summary>
    public CpgEdge this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = index * CpgEdgeData.SIZE;
            var span = _data.Slice(offset, CpgEdgeData.SIZE);
            return new CpgEdge(span, _graph);
        }
    }

    /// <summary>
    /// Enumerates all edges
    /// </summary>
    public CpgEdgeEnumerator GetEnumerator() => new(_data, _graph);

    /// <summary>
    /// Filters edges by type
    /// </summary>
    public CpgEdgeFilterEnumerator OfType(EdgeType edgeType) => new(_data, _graph, edgeType);
}

/// <summary>
/// Enumerator for CPG edges
/// </summary>
public ref struct CpgEdgeEnumerator
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;
    private int _currentIndex;

    internal CpgEdgeEnumerator(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
        _currentIndex = -1;
    }

    public CpgEdge Current
    {
        get
        {
            var offset = _currentIndex * CpgEdgeData.SIZE;
            var span = _data.Slice(offset, CpgEdgeData.SIZE);
            return new CpgEdge(span, _graph);
        }
    }

    public bool MoveNext()
    {
        _currentIndex++;
        return _currentIndex < _data.Length / CpgEdgeData.SIZE;
    }
}

/// <summary>
/// Filtered enumerator for CPG edges
/// </summary>
public ref struct CpgEdgeFilterEnumerator
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;
    private readonly EdgeType _filterType;
    private int _currentIndex;

    internal CpgEdgeFilterEnumerator(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph, EdgeType filterType)
    {
        _data = data;
        _graph = graph;
        _filterType = filterType;
        _currentIndex = -1;
    }

    public CpgEdge Current
    {
        get
        {
            var offset = _currentIndex * CpgEdgeData.SIZE;
            var span = _data.Slice(offset, CpgEdgeData.SIZE);
            return new CpgEdge(span, _graph);
        }
    }

    public bool MoveNext()
    {
        do
        {
            _currentIndex++;
            if (_currentIndex >= _data.Length / CpgEdgeData.SIZE)
                return false;

            var offset = _currentIndex * CpgEdgeData.SIZE;
            var span = _data.Slice(offset, CpgEdgeData.SIZE);
            var edge = new CpgEdge(span, _graph);
            
            if (edge.EdgeType == _filterType)
                return true;
                
        } while (true);
    }

    public CpgEdgeFilterEnumerator GetEnumerator() => this;
}