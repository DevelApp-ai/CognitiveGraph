using System;
using System.Runtime.InteropServices;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Accessors;

/// <summary>
/// Zero-allocation accessor for Packed Nodes in the graph.
/// </summary>
public readonly ref struct PackedNode
{
    private readonly ReadOnlySpan<byte> _dataSpan;
    private readonly CognitiveGraphBuffer _graph;

    internal PackedNode(ReadOnlySpan<byte> dataSpan, CognitiveGraphBuffer graph)
    {
        if (dataSpan.Length < PackedNodeData.SIZE)
            throw new ArgumentException("Data span too small for PackedNode");
        
        _dataSpan = dataSpan;
        _graph = graph;
    }

    /// <summary>
    /// Identifier for the grammar rule applied
    /// </summary>
    public ushort RuleID => MemoryMarshal.Read<ushort>(_dataSpan);

    /// <summary>
    /// Offset to the list of child Symbol/Intermediate Nodes
    /// </summary>
    public uint ChildNodesOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(4));

    /// <summary>
    /// Offset to the list of CPG edges for this specific derivation
    /// </summary>
    public uint CpgEdgesOffset => MemoryMarshal.Read<uint>(_dataSpan.Slice(8));

    /// <summary>
    /// Gets all child nodes for this packed node
    /// </summary>
    public SymbolNodeOffsetCollection GetChildNodes()
    {
        if (ChildNodesOffset == 0)
            return new SymbolNodeOffsetCollection(ReadOnlySpan<byte>.Empty, _graph);

        var listSpan = _graph.GetListSpan(ChildNodesOffset, sizeof(uint));
        return new SymbolNodeOffsetCollection(listSpan, _graph);
    }

    /// <summary>
    /// Gets all CPG edges for this derivation
    /// </summary>
    public CpgEdgeCollection GetCpgEdges()
    {
        if (CpgEdgesOffset == 0)
            return new CpgEdgeCollection(ReadOnlySpan<byte>.Empty, _graph);

        var listSpan = _graph.GetListSpan(CpgEdgesOffset, CpgEdgeData.SIZE);
        return new CpgEdgeCollection(listSpan, _graph);
    }
}

/// <summary>
/// Zero-allocation collection of packed nodes
/// </summary>
public readonly ref struct PackedNodeCollection
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;

    internal PackedNodeCollection(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
    }

    /// <summary>
    /// Number of packed nodes in the collection
    /// </summary>
    public int Count => _data.Length / PackedNodeData.SIZE;

    /// <summary>
    /// Gets a packed node by index
    /// </summary>
    public PackedNode this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var offset = index * PackedNodeData.SIZE;
            var span = _data.Slice(offset, PackedNodeData.SIZE);
            return new PackedNode(span, _graph);
        }
    }

    /// <summary>
    /// Enumerates all packed nodes
    /// </summary>
    public PackedNodeEnumerator GetEnumerator() => new(_data, _graph);
}

/// <summary>
/// Enumerator for packed nodes
/// </summary>
public ref struct PackedNodeEnumerator
{
    private readonly ReadOnlySpan<byte> _data;
    private readonly CognitiveGraphBuffer _graph;
    private int _currentIndex;

    internal PackedNodeEnumerator(ReadOnlySpan<byte> data, CognitiveGraphBuffer graph)
    {
        _data = data;
        _graph = graph;
        _currentIndex = -1;
    }

    public PackedNode Current
    {
        get
        {
            var offset = _currentIndex * PackedNodeData.SIZE;
            var span = _data.Slice(offset, PackedNodeData.SIZE);
            return new PackedNode(span, _graph);
        }
    }

    public bool MoveNext()
    {
        _currentIndex++;
        return _currentIndex < _data.Length / PackedNodeData.SIZE;
    }
}