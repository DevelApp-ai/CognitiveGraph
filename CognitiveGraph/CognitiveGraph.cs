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
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Caching.Memory;
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
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly IMemoryCache _cache;
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
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 1000 // Cache up to 1000 items
        });
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
            // Create memory-mapped file
            _mmf = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, "CognitiveGraph", 0, MemoryMappedFileAccess.Read);
            _accessor = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            
            // Create buffer over memory-mapped data
            unsafe
            {
                var ptr = (byte*)_accessor.SafeMemoryMappedViewHandle.DangerousGetHandle();
                var length = new FileInfo(filePath).Length;
                var span = new ReadOnlySpan<byte>(ptr, (int)length);
                _buffer = new CognitiveGraphBuffer(span.ToArray(), takeOwnership: false);
            }
            
            if (!_buffer.IsValidGraph())
                throw new ArgumentException($"File does not contain a valid Cognitive Graph: {filePath}");
            
            _header = _buffer.GetHeader();
            _cache = new MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1000 // Cache up to 1000 items for disk-backed graphs
            });
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
    /// Finds all node offsets that contain the specified byte offset using the spatial index
    /// </summary>
    public List<uint> FindNodesAt(uint byteOffset)
    {
        if (_header.IntervalTreeOffset == 0)
        {
            // No spatial index available, return empty list
            return new List<uint>();
        }

        // Try to get from cache first
        var cacheKey = $"spatial_{byteOffset}";
        if (_cache.TryGetValue(cacheKey, out object? cachedObj) && cachedObj is List<uint> cachedResult)
        {
            return cachedResult;
        }

        // Load interval tree from buffer
        var treeStart = (int)_header.IntervalTreeOffset;
        var remainingBuffer = _buffer.Slice(treeStart);
        var intervalTree = IntervalTree.Deserialize(remainingBuffer);
        
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
    /// Gets the underlying buffer (for advanced scenarios)
    /// </summary>
    internal CognitiveGraphBuffer GetBuffer() => _buffer;

    public void Dispose()
    {
        if (!_disposed)
        {
            _cache?.Dispose();
            _buffer?.Dispose();
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