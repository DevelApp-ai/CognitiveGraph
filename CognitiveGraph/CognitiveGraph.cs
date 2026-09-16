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
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using CognitiveGraph.Accessors;
using CognitiveGraph.Buffer;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph.QueryEngine;

namespace CognitiveGraph;

/// <summary>
/// Main entry point for the Cognitive Graph API.
/// Provides high-level access to the zero-copy graph structure.
/// </summary>
public sealed class CognitiveGraph : IDisposable
{
    private readonly IGraphBuffer? _bufferV2;
    private readonly CognitiveGraphBuffer? _bufferV1;
    private readonly GraphHeader? _headerV1;
    private readonly GraphHeaderV2? _headerV2;
    private readonly SchemaVersion _schemaVersion;
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly IMemoryCache _cache;
    private IntervalTree? _spatialIndex;
    private bool _disposed;

    /// <summary>
    /// Gets the schema version of this graph
    /// </summary>
    public SchemaVersion SchemaVersion => _schemaVersion;

    /// <summary>
    /// Creates a new Cognitive Graph from an existing buffer
    /// </summary>
    public CognitiveGraph(CognitiveGraphBuffer buffer)
    {
        _bufferV1 = buffer ?? throw new ArgumentNullException(nameof(buffer));
        
        if (!_bufferV1.IsValidGraph())
            throw new ArgumentException("Buffer does not contain a valid Cognitive Graph", nameof(buffer));
        
        // Read preamble to determine schema version
        var preamble = MemoryMarshal.Read<GraphHeaderPreamble>(_bufferV1.AsSpan());
        _schemaVersion = (SchemaVersion)preamble.Version;
        
        if (_schemaVersion == SchemaVersion.V1)
        {
            _headerV1 = _bufferV1.GetHeader();
        }
        else if (_schemaVersion == SchemaVersion.V2)
        {
            // For V2, read the header and set up the buffer for V2 operations
            // IMPORTANT: For in-memory graphs (byte arrays), we reuse the CompactGraphBuffer
            // but this limits V2 functionality - full V2 accessor support (SymbolNode64)
            // requires UniversalGraphBuffer which is file-based. In-memory V2 graphs
            // can still use GetSourceText() and GetStatistics() but not GetRootNodeV2().
            _headerV2 = MemoryMarshal.Read<GraphHeaderV2>(_bufferV1.AsSpan());
            _bufferV2 = _bufferV1; // Reuse V1 buffer for in-memory V2 graphs (limited functionality)
        }
        else
        {
            throw new ArgumentException($"Unsupported schema version: {preamble.Version}");
        }
        
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    /// <summary>
    /// Creates a Cognitive Graph from a memory-mapped file for large-scale persistence
    /// </summary>
    public CognitiveGraph(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
        
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Graph file not found: {filePath}");

        try
        {
            var fileLength = new FileInfo(filePath).Length;
            
            // Create memory-mapped file
            _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            
            // Read preamble to determine schema version
            GraphHeaderPreamble preamble;
            unsafe
            {
                byte* ptr = null;
                _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                preamble = *(GraphHeaderPreamble*)ptr;
                _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            }
            
            _schemaVersion = (SchemaVersion)preamble.Version;
            
            if (_schemaVersion == SchemaVersion.V1)
            {
                // V1: Use a span-based buffer pinned over the memory-mapped view (files < 4GB).
                // The buffer acquires the view pointer once (AcquirePointer) and releases it on
                // Dispose, serving read-only spans directly from the mapped view - so loading a
                // V1 graph file no longer copies the whole file into a managed byte[].
                _bufferV1 = new CognitiveGraphBuffer(_mmf, _accessor, fileLength);
                
                if (!_bufferV1.IsValidGraph())
                {
                    _bufferV1.Dispose();
                    throw new ArgumentException($"File does not contain a valid Cognitive Graph: {filePath}");
                }
                
                _headerV1 = _bufferV1.GetHeader();
            }
            else if (_schemaVersion == SchemaVersion.V2)
            {
                // V2: Use unsafe pointer-based buffer (for files >= 4GB)
                _bufferV2 = new UniversalGraphBuffer(_mmf, _accessor, fileLength);
                
                if (!_bufferV2.IsValidGraph())
                    throw new ArgumentException($"File does not contain a valid Cognitive Graph: {filePath}");
                
                var universalBuffer = (UniversalGraphBuffer)_bufferV2;
                _headerV2 = universalBuffer.GetHeaderV2();
            }
            else
            {
                throw new ArgumentException($"Unsupported schema version: {preamble.Version}");
            }
            
            _cache = new MemoryCache(new MemoryCacheOptions());
        }
        catch
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
            throw;
        }
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
    /// Gets the graph header information (V1 schema)
    /// </summary>
    [Obsolete("Use GetHeader() or GetHeaderV2() based on SchemaVersion")]
    public GraphHeader Header => _headerV1 ?? throw new InvalidOperationException("Graph is using V2 schema, use GetHeaderV2()");

    /// <summary>
    /// Gets the V1 graph header
    /// </summary>
    public GraphHeader? GetHeader() => _headerV1;

    /// <summary>
    /// Gets the V2 graph header
    /// </summary>
    public GraphHeaderV2? GetHeaderV2() => _headerV2;

    /// <summary>
    /// Gets the root node of the parse tree (V1 schema)
    /// </summary>
    public SymbolNode GetRootNode()
    {
        if (_schemaVersion != SchemaVersion.V1 || _bufferV1 == null || _headerV1 == null)
            throw new InvalidOperationException("GetRootNode() is only available for V1 schema. Use GetRootNodeV2() for V2.");
        
        var rootSpan = _bufferV1.Slice((int)_headerV1.Value.RootNodeOffset, SymbolNodeData.SIZE);
        return new SymbolNode(rootSpan, _bufferV1);
    }

    /// <summary>
    /// Gets the root node of the parse tree (V2 schema)
    /// </summary>
    public SymbolNode64 GetRootNodeV2()
    {
        if (_schemaVersion != SchemaVersion.V2 || _bufferV2 == null || _headerV2 == null)
            throw new InvalidOperationException("GetRootNodeV2() is only available for V2 schema. Use GetRootNode() for V1.");
        
        // For V2 graphs loaded from byte arrays (in-memory), we can still access via IGraphBuffer interface
        if (_bufferV2 is UniversalGraphBuffer universalBuffer)
        {
            return new SymbolNode64(universalBuffer, (long)_headerV2.Value.RootNodeOffset);
        }
        else
        {
            // For in-memory V2 graphs, we need to create a temporary UniversalGraphBuffer
            // This is a limitation - true V2 support works best with file-based graphs
            throw new NotSupportedException("V2 accessor methods require file-based graphs. Use GetSourceText() and GetStatistics() for in-memory V2 graphs.");
        }
    }

    /// <summary>
    /// Gets the original source text
    /// </summary>
    public string GetSourceText()
    {
        if (_schemaVersion == SchemaVersion.V1 && _bufferV1 != null && _headerV1 != null)
        {
            var sourceBytes = _bufferV1.Slice((int)_headerV1.Value.SourceTextOffset, (int)_headerV1.Value.SourceTextLength);
            return System.Text.Encoding.UTF8.GetString(sourceBytes);
        }
        else if (_schemaVersion == SchemaVersion.V2 && _bufferV2 != null && _headerV2 != null)
        {
            var sourceBytes = _bufferV2.GetSpan((long)_headerV2.Value.SourceTextOffset, (int)_headerV2.Value.SourceTextLength);
            return System.Text.Encoding.UTF8.GetString(sourceBytes);
        }
        
        throw new InvalidOperationException("Cannot get source text: invalid schema state");
    }

    /// <summary>
    /// Gets a symbol node at the specified offset (V1 schema)
    /// </summary>
    public SymbolNode GetNodeAt(uint offset)
    {
        if (_schemaVersion != SchemaVersion.V1 || _bufferV1 == null)
            throw new InvalidOperationException("GetNodeAt() is only available for V1 schema. Use GetNodeAtV2() for V2.");
        
        var nodeSpan = _bufferV1.Slice((int)offset, SymbolNodeData.SIZE);
        return new SymbolNode(nodeSpan, _bufferV1);
    }

    /// <summary>
    /// Gets a symbol node at the specified offset (V2 schema)
    /// </summary>
    public SymbolNode64 GetNodeAtV2(ulong offset)
    {
        if (_schemaVersion != SchemaVersion.V2 || _bufferV2 == null)
            throw new InvalidOperationException("GetNodeAtV2() is only available for V2 schema. Use GetNodeAt() for V1.");
        
        if (_bufferV2 is UniversalGraphBuffer universalBuffer)
            return new SymbolNode64(universalBuffer, (long)offset);

        // In-memory V2 graphs (byte arrays) reuse the V1 buffer and cannot
        // provide V2 accessors. Match the GetRootNodeV2() contract instead
        // of throwing an InvalidCastException from the unconditional cast.
        throw new NotSupportedException("V2 accessor methods require file-based graphs. Use GetSourceText() and GetStatistics() for in-memory V2 graphs.");
    }

    /// <summary>
    /// Checks if the graph represents a fully parsed source file
    /// </summary>
    public bool IsFullyParsed
    {
        get
        {
            if (_schemaVersion == SchemaVersion.V1 && _headerV1 != null)
                return ((GraphFlags)_headerV1.Value.Flags & GraphFlags.FullyParsed) != 0;
            else if (_schemaVersion == SchemaVersion.V2 && _headerV2 != null)
                return ((GraphFlags)_headerV2.Value.Flags & GraphFlags.FullyParsed) != 0;
            return false;
        }
    }

    /// <summary>
    /// Checks if the graph contains syntax errors
    /// </summary>
    public bool HasSyntaxErrors
    {
        get
        {
            if (_schemaVersion == SchemaVersion.V1 && _headerV1 != null)
                return ((GraphFlags)_headerV1.Value.Flags & GraphFlags.HasSyntaxErrors) != 0;
            else if (_schemaVersion == SchemaVersion.V2 && _headerV2 != null)
                return ((GraphFlags)_headerV2.Value.Flags & GraphFlags.HasSyntaxErrors) != 0;
            return false;
        }
    }

    /// <summary>
    /// Gets statistics about the graph
    /// </summary>
    public GraphStatistics GetStatistics()
    {
        if (_schemaVersion == SchemaVersion.V1 && _headerV1 != null && _bufferV1 != null)
        {
            return new GraphStatistics
            {
                NodeCount = _headerV1.Value.NodeCount,
                EdgeCount = _headerV1.Value.EdgeCount,
                SourceLength = _headerV1.Value.SourceTextLength,
                BufferSize = (uint)_bufferV1.Length
            };
        }
        else if (_schemaVersion == SchemaVersion.V2 && _headerV2 != null && _bufferV2 != null)
        {
            return new GraphStatistics
            {
                NodeCount = (uint)Math.Min(_headerV2.Value.NodeCount, uint.MaxValue),
                EdgeCount = (uint)Math.Min(_headerV2.Value.EdgeCount, uint.MaxValue),
                SourceLength = (uint)Math.Min(_headerV2.Value.SourceTextLength, uint.MaxValue),
                BufferSize = (uint)Math.Min(_bufferV2.Length, uint.MaxValue)
            };
        }
        
        throw new InvalidOperationException("Cannot get statistics: invalid schema state");
    }

    /// <summary>
    /// Finds all node offsets that contain the specified byte offset using the spatial index
    /// </summary>
    public List<uint> FindNodesAt(uint byteOffset)
    {
        uint intervalTreeOffset = 0;
        ReadOnlySpan<byte> remainingBuffer;
        
        if (_schemaVersion == SchemaVersion.V1 && _headerV1 != null && _bufferV1 != null)
        {
            intervalTreeOffset = _headerV1.Value.IntervalTreeOffset;
            if (intervalTreeOffset == 0)
                return new List<uint>();
            
            remainingBuffer = _bufferV1.Slice((int)intervalTreeOffset);
        }
        else if (_schemaVersion == SchemaVersion.V2 && _headerV2 != null && _bufferV2 != null)
        {
            if (_headerV2.Value.IntervalTreeOffset == 0)
                return new List<uint>();
            
            var treeOffset = _headerV2.Value.IntervalTreeOffset;
            var maxLength = Math.Min(int.MaxValue, _bufferV2.Length - (long)treeOffset);
            remainingBuffer = _bufferV2.GetSpan((long)treeOffset, (int)maxLength);
        }
        else
        {
            return new List<uint>();
        }

        // Try to get from cache first
        var cacheKey = $"spatial_{byteOffset}";
        if (_cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is List<uint> cachedResult)
        {
            return cachedResult;
        }

        // Deserialize the interval tree once and reuse it for every subsequent query.
        // The buffer is immutable after load, so the tree never needs rebuilding.
        // Benign race under concurrent first callers: reference assignment is atomic and
        // the losing caller's tree instance is simply garbage-collected.
        var intervalTree = _spatialIndex;
        if (intervalTree == null)
        {
            intervalTree = IntervalTree.Deserialize(remainingBuffer);
            _spatialIndex = intervalTree;
        }
        
        // Find node offsets at the specified location
        var result = intervalTree.FindNodesAt(byteOffset);
        
        // Cache the result
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
        
        return result;
    }

    /// <summary>
    /// Delegate for processing symbol nodes (works with ref structs)
    /// </summary>
    public delegate void NodeProcessor(in SymbolNode node);

    /// <summary>
    /// Gets symbol nodes at the specified byte offset using the spatial index
    /// </summary>
    public void ProcessNodesAt(uint byteOffset, NodeProcessor nodeProcessor)
    {
        var offsets = FindNodesAt(byteOffset);
        foreach (var offset in offsets)
        {
            var node = GetNodeAt(offset);
            nodeProcessor(in node);
        }
    }

    /// <summary>
    /// Executes a GraphQL query against the graph and returns matching node offsets
    /// </summary>
    public async Task<List<uint>> QueryAsync(string graphQLQuery)
    {
        if (string.IsNullOrWhiteSpace(graphQLQuery))
            return new List<uint>();

        var queryEngine = new GraphQLQueryEngine(this);
        return await queryEngine.ExecuteQueryAsync(graphQLQuery);
    }

    /// <summary>
    /// Synchronous version of Query for simpler usage
    /// </summary>
    public List<uint> Query(string graphQLQuery)
    {
        return QueryAsync(graphQLQuery).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Gets the underlying buffer (for advanced scenarios)
    /// </summary>
    [Obsolete("Use GetBufferV1() or GetBufferV2() based on SchemaVersion")]
    internal CognitiveGraphBuffer? GetBuffer() => _bufferV1;

    /// <summary>
    /// Gets the V1 buffer
    /// </summary>
    internal CognitiveGraphBuffer? GetBufferV1() => _bufferV1;

    /// <summary>
    /// Gets the V2 buffer
    /// </summary>
    internal IGraphBuffer? GetBufferV2() => _bufferV2;

    /// <summary>
    /// Upgrades a V1 graph file to V2 format.
    /// Performs a full recursive traversal of the V1 graph and re-emits every symbol
    /// node, packed node (with children), property and CPG edge through a V2 builder.
    /// Shared nodes are migrated exactly once (memoized by V1 offset) so the SPPF
    /// sharing structure of the source graph is preserved, and in-progress guards
    /// reject cyclic (malformed) graphs instead of recursing forever. The interval
    /// tree is rebuilt automatically by the builder as nodes are written.
    /// </summary>
    /// <param name="inputPath">Path to the input V1 graph file</param>
    /// <param name="outputPath">Path for the output V2 graph file</param>
    public static void Upgrade(string inputPath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            throw new ArgumentException("Input path cannot be null or empty", nameof(inputPath));
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path cannot be null or empty", nameof(outputPath));
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Input file not found: {inputPath}");

        // Open input V1 graph
        using var inputGraph = new CognitiveGraph(inputPath);
        
        if (inputGraph.SchemaVersion != SchemaVersion.V1)
            throw new InvalidOperationException("Upgrade only works on V1 graphs. Input is already V2 or unsupported version.");

        // Get V1 header and data
        var v1Header = inputGraph.GetHeader();
        if (!v1Header.HasValue)
            throw new InvalidOperationException("Failed to read V1 header");

        var bufferV1 = inputGraph.GetBufferV1()
            ?? throw new InvalidOperationException("Failed to access the V1 graph buffer");
        var sourceText = inputGraph.GetSourceText();

        // Create V2 builder
        var options = GraphBuilderOptions.Universal();
        using var builder = new CognitiveGraphBuilder(options);

        // Memoization tables (V1 offset -> V2 offset) preserve SPPF node sharing;
        // the in-progress sets guard against cycles in malformed graphs.
        var migratedSymbolNodes = new Dictionary<uint, ulong>();
        var migratedPackedNodes = new Dictionary<uint, ulong>();
        var inProgressSymbolNodes = new HashSet<uint>();
        var inProgressPackedNodes = new HashSet<uint>();

        // Helpers over the V1 list layout: [uint count][items...]
        uint ListCount(uint listOffset) => listOffset == 0 ? 0u : bufferV1.Read<uint>(listOffset);

        uint ListItemAt(uint listOffset, int index) =>
            bufferV1.Read<uint>(listOffset + sizeof(uint) + (uint)(index * sizeof(uint)));

        (string key, PropertyValueType type, object value) ReadProperty(PropertyData propertyData)
        {
            var key = bufferV1.ReadString(propertyData.KeyOffset);

            var header = bufferV1.Read<PropertyValueHeader>(propertyData.ValueOffset);
            var valueSpan = bufferV1.Slice(
                (int)(propertyData.ValueOffset + PropertyValueHeader.SIZE), (int)header.Length);

            object value = header.Type switch
            {
                PropertyValueType.String => System.Text.Encoding.UTF8.GetString(valueSpan),
                PropertyValueType.Int32 => MemoryMarshal.Read<int>(valueSpan),
                PropertyValueType.UInt32 => MemoryMarshal.Read<uint>(valueSpan),
                PropertyValueType.Boolean => valueSpan.Length > 0 && valueSpan[0] != 0,
                PropertyValueType.Double => MemoryMarshal.Read<double>(valueSpan),
                _ => throw new NotSupportedException(
                    $"Property '{key}' uses value type {header.Type}, which the V2 builder does not support yet (see issue #15).")
            };

            return (key, header.Type, value);
        }

        List<(string key, PropertyValueType type, object value)>? ReadPropertyList(uint listOffset)
        {
            var count = (int)ListCount(listOffset);
            if (count == 0)
                return null;

            var properties = new List<(string, PropertyValueType, object)>(count);
            for (int i = 0; i < count; i++)
            {
                var propertyOffset = listOffset + sizeof(uint) + (uint)(i * PropertyData.SIZE);
                properties.Add(ReadProperty(bufferV1.Read<PropertyData>(propertyOffset)));
            }

            return properties;
        }

        ulong MigratePackedNode(uint packedNodeOffsetV1)
        {
            if (migratedPackedNodes.TryGetValue(packedNodeOffsetV1, out var existingV2))
                return existingV2;
            if (!inProgressPackedNodes.Add(packedNodeOffsetV1))
                throw new InvalidOperationException($"Cycle detected at packed node offset {packedNodeOffsetV1}.");

            var packedData = bufferV1.Read<PackedNodeData>(packedNodeOffsetV1);

            // Migrate child symbol nodes
            List<uint>? childNodeOffsets = null;
            var childCount = (int)ListCount(packedData.ChildNodesOffset);
            if (childCount > 0)
            {
                childNodeOffsets = new List<uint>(childCount);
                for (int i = 0; i < childCount; i++)
                {
                    childNodeOffsets.Add((uint)MigrateSymbolNode(ListItemAt(packedData.ChildNodesOffset, i)));
                }
            }

            // Migrate CPG edges, re-targeting each edge to the migrated target node
            List<CpgEdgeData>? cpgEdges = null;
            var edgeCount = (int)ListCount(packedData.CpgEdgesOffset);
            if (edgeCount > 0)
            {
                cpgEdges = new List<CpgEdgeData>(edgeCount);
                for (int i = 0; i < edgeCount; i++)
                {
                    var edgeOffset = packedData.CpgEdgesOffset + sizeof(uint) + (uint)(i * CpgEdgeData.SIZE);
                    var edgeData = bufferV1.Read<CpgEdgeData>(edgeOffset);

                    var targetNodeOffsetV2 = MigrateSymbolNode(edgeData.TargetNodeOffset);
                    var edgeProperties = ReadPropertyList(edgeData.PropertiesOffset);

                    cpgEdges.Add(new CpgEdgeData(
                        edgeData.EdgeType,
                        (uint)targetNodeOffsetV2,
                        edgeProperties != null ? (uint)builder.WritePropertyList(edgeProperties) : 0u,
                        edgeData.Reserved));
                }
            }

            var packedNodeOffsetV2 = builder.WritePackedNode(packedData.RuleID, childNodeOffsets, cpgEdges);

            inProgressPackedNodes.Remove(packedNodeOffsetV1);
            migratedPackedNodes[packedNodeOffsetV1] = packedNodeOffsetV2;
            return packedNodeOffsetV2;
        }

        ulong MigrateSymbolNode(uint nodeOffsetV1)
        {
            if (migratedSymbolNodes.TryGetValue(nodeOffsetV1, out var existingV2))
                return existingV2;
            if (!inProgressSymbolNodes.Add(nodeOffsetV1))
                throw new InvalidOperationException($"Cycle detected at symbol node offset {nodeOffsetV1}.");

            var node = inputGraph.GetNodeAt(nodeOffsetV1);

            // Migrate packed nodes (derivations)
            List<uint>? packedNodeOffsets = null;
            var packedCount = (int)ListCount(node.PackedNodesOffset);
            if (packedCount > 0)
            {
                packedNodeOffsets = new List<uint>(packedCount);
                for (int i = 0; i < packedCount; i++)
                {
                    packedNodeOffsets.Add((uint)MigratePackedNode(ListItemAt(node.PackedNodesOffset, i)));
                }
            }

            // Migrate properties
            var properties = ReadPropertyList(node.PropertiesOffset);

            var nodeOffsetV2 = builder.WriteSymbolNode(
                node.SymbolID,
                node.NodeType,
                node.SourceStart,
                node.SourceLength,
                packedNodeOffsets,
                properties);

            inProgressSymbolNodes.Remove(nodeOffsetV1);
            migratedSymbolNodes[nodeOffsetV1] = nodeOffsetV2;
            return nodeOffsetV2;
        }

        // Migrate the whole graph starting at the root
        var rootNodeOffsetV2 = MigrateSymbolNode(v1Header.Value.RootNodeOffset);

        // Build and write to file
        using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        builder.Build(outputStream, (uint)rootNodeOffsetV2, sourceText);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache?.Dispose();
            _bufferV1?.Dispose();
            _bufferV2?.Dispose();
            _accessor?.Dispose();
            _mmf?.Dispose();
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