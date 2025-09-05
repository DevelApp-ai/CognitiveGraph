using System;
using CognitiveGraph.Accessors;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph;

/// <summary>
/// Main entry point for the Cognitive Graph API.
/// Provides high-level access to the zero-copy graph structure.
/// </summary>
public sealed class CognitiveGraph : IDisposable
{
    private readonly CognitiveGraphBuffer _buffer;
    private readonly GraphHeader _header;
    private bool _disposed;

    /// <summary>
    /// Creates a new Cognitive Graph from an existing buffer
    /// </summary>
    public CognitiveGraph(CognitiveGraphBuffer buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        
        if (!_buffer.IsValidGraph())
            throw new ArgumentException("Buffer does not contain a valid Cognitive Graph", nameof(buffer));
        
        _header = _buffer.GetHeader();
    }

    /// <summary>
    /// Creates a Cognitive Graph from a byte array
    /// </summary>
    public static CognitiveGraph FromBytes(byte[] data)
    {
        var buffer = new CognitiveGraphBuffer(data, takeOwnership: false);
        return new CognitiveGraph(buffer);
    }

    /// <summary>
    /// Gets the graph header information
    /// </summary>
    public GraphHeader Header => _header;

    /// <summary>
    /// Gets the root node of the parse tree
    /// </summary>
    public SymbolNode GetRootNode()
    {
        var rootSpan = _buffer.Slice((int)_header.RootNodeOffset, SymbolNodeData.SIZE);
        return new SymbolNode(rootSpan, _buffer);
    }

    /// <summary>
    /// Gets the original source text
    /// </summary>
    public string GetSourceText()
    {
        var sourceBytes = _buffer.Slice((int)_header.SourceTextOffset, (int)_header.SourceTextLength);
        return System.Text.Encoding.UTF8.GetString(sourceBytes);
    }

    /// <summary>
    /// Gets a symbol node at the specified offset
    /// </summary>
    public SymbolNode GetNodeAt(uint offset)
    {
        var nodeSpan = _buffer.Slice((int)offset, SymbolNodeData.SIZE);
        return new SymbolNode(nodeSpan, _buffer);
    }

    /// <summary>
    /// Checks if the graph represents a fully parsed source file
    /// </summary>
    public bool IsFullyParsed => ((GraphFlags)_header.Flags & GraphFlags.FullyParsed) != 0;

    /// <summary>
    /// Checks if the graph contains syntax errors
    /// </summary>
    public bool HasSyntaxErrors => ((GraphFlags)_header.Flags & GraphFlags.HasSyntaxErrors) != 0;

    /// <summary>
    /// Gets statistics about the graph
    /// </summary>
    public GraphStatistics GetStatistics()
    {
        return new GraphStatistics
        {
            NodeCount = _header.NodeCount,
            EdgeCount = _header.EdgeCount,
            SourceLength = _header.SourceTextLength,
            BufferSize = (uint)_buffer.Length
        };
    }

    /// <summary>
    /// Gets the underlying buffer (for advanced scenarios)
    /// </summary>
    internal CognitiveGraphBuffer GetBuffer() => _buffer;

    public void Dispose()
    {
        if (!_disposed)
        {
            _buffer?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Flags for graph properties
/// </summary>
[Flags]
public enum GraphFlags : ushort
{
    None = 0,
    FullyParsed = 1,
    HasSyntaxErrors = 2,
    HasSemanticAnalysis = 4,
    HasTypeInformation = 8
}

/// <summary>
/// Statistics about the graph
/// </summary>
public readonly struct GraphStatistics
{
    public uint NodeCount { get; init; }
    public uint EdgeCount { get; init; }
    public uint SourceLength { get; init; }
    public uint BufferSize { get; init; }

    public double NodesPerKb => NodeCount / (BufferSize / 1024.0);
    public double EdgesPerKb => EdgeCount / (BufferSize / 1024.0);
}