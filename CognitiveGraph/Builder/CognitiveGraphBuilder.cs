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
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Builder;

/// <summary>
/// Builder for constructing Cognitive Graphs incrementally.
/// Uses a resizable buffer approach for dynamic construction.
/// </summary>
public sealed class CognitiveGraphBuilder : IDisposable
{
    private readonly List<byte> _buffer;
    private readonly Dictionary<string, uint> _stringTable;
    private readonly IntervalTree _intervalTree;
    private uint _currentOffset;
    private ulong _currentOffsetV2;
    private GraphHeader _header;
    private GraphHeaderV2 _headerV2;
    private readonly GraphBuilderOptions _options;
    private bool _disposed;

    public CognitiveGraphBuilder() : this(new GraphBuilderOptions())
    {
    }

    public CognitiveGraphBuilder(GraphBuilderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _buffer = new List<byte>(_options.InitialCapacity);
        _stringTable = new Dictionary<string, uint>();
        _intervalTree = new IntervalTree();
        
        if (_options.Schema == SchemaVersion.V1)
        {
            _currentOffset = 0;
            // Reserve space for V1 header (will be written last)
            _buffer.AddRange(new byte[GraphHeader.SIZE]);
            _currentOffset = GraphHeader.SIZE;
        }
        else // V2
        {
            _currentOffsetV2 = 0;
            // Reserve space for V2 header (will be written last)
            _buffer.AddRange(new byte[GraphHeaderV2.SIZE]);
            _currentOffsetV2 = GraphHeaderV2.SIZE;
        }
    }

    /// <summary>
    /// Writes a string to the buffer and returns its offset
    /// </summary>
    public ulong WriteString(string value)
    {
        if (_stringTable.TryGetValue(value, out var existingOffset))
            return existingOffset;

        ulong offset = _options.Schema == SchemaVersion.V1 ? _currentOffset : _currentOffsetV2;
        var bytes = Encoding.UTF8.GetBytes(value);
        
        _buffer.AddRange(bytes);
        _buffer.Add(0); // null terminator
        
        if (_options.Schema == SchemaVersion.V1)
        {
            _currentOffset += (uint)(bytes.Length + 1);
        }
        else
        {
            _currentOffsetV2 += (ulong)(bytes.Length + 1);
        }
        
        _stringTable[value] = (uint)offset; // Store as uint for compatibility
        return offset;
    }

    /// <summary>
    /// Writes a property value to the buffer
    /// </summary>
    public ulong WritePropertyValue(PropertyValueType type, object value)
    {
        ulong offset = _options.Schema == SchemaVersion.V1 ? _currentOffset : _currentOffsetV2;
        
        // Write header
        var header = new PropertyValueHeader(type, GetValueLength(type, value));
        WriteStruct(header);

        // Write value data
        switch (type)
        {
            case PropertyValueType.String:
                var stringBytes = Encoding.UTF8.GetBytes((string)value);
                _buffer.AddRange(stringBytes);
                if (_options.Schema == SchemaVersion.V1)
                    _currentOffset += (uint)stringBytes.Length;
                else
                    _currentOffsetV2 += (ulong)stringBytes.Length;
                break;
                
            case PropertyValueType.Int32:
                WriteStruct((int)value);
                break;
                
            case PropertyValueType.UInt32:
                WriteStruct((uint)value);
                break;
                
            case PropertyValueType.Boolean:
                _buffer.Add((byte)((bool)value ? 1 : 0));
                _currentOffset += 1;
                break;
                
            case PropertyValueType.Double:
                WriteStruct((double)value);
                break;
                
            default:
                throw new ArgumentException($"Unsupported property value type: {type}");
        }

        return offset;
    }

    /// <summary>
    /// Writes a list of items to the buffer
    /// </summary>
    public uint WriteList<T>(IReadOnlyList<T> items, Func<T, uint> itemWriter)
    {
        var offset = _currentOffset;
        
        // Write count
        WriteStruct((uint)items.Count);
        
        // Write items
        foreach (var item in items)
        {
            itemWriter(item);
        }
        
        return offset;
    }

    /// <summary>
    /// Writes a list of items to the buffer using V2 schema (64-bit offsets)
    /// </summary>
    private ulong WriteListV2<T>(IReadOnlyList<T> items, Func<T, ulong> itemWriter)
    {
        var offset = _currentOffsetV2;
        
        // Write count (64-bit for V2)
        WriteStruct((ulong)items.Count);
        
        // Write items
        foreach (var item in items)
        {
            itemWriter(item);
        }
        
        return offset;
    }

    /// <summary>
    /// Writes a symbol node to the buffer
    /// </summary>
    public uint WriteSymbolNode(ushort symbolId, ushort nodeType, uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null, IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
    {
        if (_options.Schema == SchemaVersion.V2)
        {
            // For V2, delegate to the V2-specific method with expanded types
            return (uint)WriteSymbolNodeV2(symbolId, nodeType, sourceStart, sourceLength, packedNodeOffsets, properties);
        }
        
        // V1 implementation
        // Write packed nodes list
        var packedNodesOffset = packedNodeOffsets?.Count > 0 
            ? WriteList(packedNodeOffsets, o => { WriteStruct(o); return 0; })
            : 0u;

        // Write properties list
        var propertiesOffset = 0u;
        if (properties?.Count > 0)
        {
            var propertyDataList = new List<PropertyData>();
            foreach (var (key, type, value) in properties)
            {
                var keyOffset = WriteString(key);
                var valueOffset = WritePropertyValue(type, value);
                // PropertyData uses uint offsets (V1 schema), cast for compatibility
                propertyDataList.Add(new PropertyData((uint)keyOffset, (uint)valueOffset));
            }
            
            propertiesOffset = WriteList(propertyDataList, p => { WriteStruct(p); return 0; });
        }

        // Now write the symbol node data and capture its offset
        var nodeOffset = _currentOffset;
        var nodeData = new SymbolNodeData(symbolId, nodeType, sourceStart, sourceLength, packedNodesOffset, propertiesOffset);
        WriteStruct(nodeData);
        
        // Add to interval tree for spatial indexing
        _intervalTree.Add(sourceStart, sourceStart + sourceLength - 1, nodeOffset);
        
        return nodeOffset;
    }

    /// <summary>
    /// Writes a symbol node to the buffer using V2 schema
    /// </summary>
    private ulong WriteSymbolNodeV2(uint symbolId, uint nodeType, uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null, IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
    {
        // Write packed nodes list with 64-bit offsets
        var packedNodesOffsetV2 = packedNodeOffsets?.Count > 0 
            ? WriteListV2(packedNodeOffsets, o => { WriteStruct((ulong)o); return 0UL; })
            : 0UL;

        // Write properties list
        var propertiesOffsetV2 = 0UL;
        if (properties?.Count > 0)
        {
            var propertyDataList = new List<PropertyData>();
            foreach (var (key, type, value) in properties)
            {
                var keyOffset = WriteString(key);
                var valueOffset = WritePropertyValue(type, value);
                // PropertyData uses uint offsets, cast for V1 compatibility
                // Note: For true V2 support, would need PropertyDataV2 structure
                propertyDataList.Add(new PropertyData((uint)keyOffset, (uint)valueOffset));
            }
            
            propertiesOffsetV2 = WriteListV2(propertyDataList, p => { WriteStruct(p); return 0UL; });
        }

        // Now write the symbol node data and capture its offset
        var nodeOffset = _currentOffsetV2;
        var nodeData = new SymbolNodeDataV2(symbolId, nodeType, sourceStart, sourceLength, packedNodesOffsetV2, propertiesOffsetV2);
        WriteStruct(nodeData);
        
        // Add to interval tree for spatial indexing
        _intervalTree.Add(sourceStart, sourceStart + sourceLength - 1, (uint)nodeOffset);
        
        return nodeOffset;
    }

    /// <summary>
    /// Writes a packed node to the buffer
    /// </summary>
    public uint WritePackedNode(ushort ruleId, IReadOnlyList<uint>? childNodeOffsets = null, IReadOnlyList<CpgEdgeData>? cpgEdges = null)
    {
        if (_options.Schema == SchemaVersion.V2)
        {
            return (uint)WritePackedNodeV2(ruleId, childNodeOffsets, cpgEdges);
        }
        
        // V1 implementation
        // Write child nodes list
        var childNodesOffset = childNodeOffsets?.Count > 0 
            ? WriteList(childNodeOffsets, o => { WriteStruct(o); return 0; })
            : 0u;

        // Write CPG edges list
        var cpgEdgesOffset = cpgEdges?.Count > 0 
            ? WriteList(cpgEdges, e => { WriteStruct(e); return 0; })
            : 0u;

        // Now write the packed node data and capture its offset
        var nodeOffset = _currentOffset;
        var nodeData = new PackedNodeData(ruleId, childNodesOffset, cpgEdgesOffset);
        WriteStruct(nodeData);
        
        return nodeOffset;
    }

    /// <summary>
    /// Writes a packed node to the buffer using V2 schema
    /// </summary>
    private ulong WritePackedNodeV2(uint ruleId, IReadOnlyList<uint>? childNodeOffsets = null, IReadOnlyList<CpgEdgeData>? cpgEdges = null)
    {
        // Write child nodes list with 64-bit offsets
        var childNodesOffsetV2 = childNodeOffsets?.Count > 0 
            ? WriteListV2(childNodeOffsets, o => { WriteStruct((ulong)o); return 0UL; })
            : 0UL;

        // Write CPG edges list
        var cpgEdgesOffsetV2 = cpgEdges?.Count > 0 
            ? WriteListV2(cpgEdges, e => { WriteStruct(e); return 0UL; })
            : 0UL;

        // Now write the packed node data and capture its offset
        var nodeOffset = _currentOffsetV2;
        var nodeData = new PackedNodeDataV2(ruleId, childNodesOffsetV2, cpgEdgesOffsetV2);
        WriteStruct(nodeData);
        
        return nodeOffset;
    }

    /// <summary>
    /// Builds the final graph buffer in memory
    /// </summary>
    public CognitiveGraphBuffer Build(uint rootNodeOffset, string sourceText)
    {
        if (_options.Schema == SchemaVersion.V2)
        {
            return BuildV2(rootNodeOffset, sourceText);
        }
        
        // V1 implementation
        // Write source text
        var sourceTextOffset = _currentOffset;
        var sourceBytes = Encoding.UTF8.GetBytes(sourceText);
        _buffer.AddRange(sourceBytes);
        _currentOffset += (uint)sourceBytes.Length;

        // Write interval tree index
        var intervalTreeOffset = _currentOffset;
        var intervalTreeBytes = _intervalTree.Serialize();
        _buffer.AddRange(intervalTreeBytes);
        _currentOffset += (uint)intervalTreeBytes.Length;

        // Create and write header
        _header = new GraphHeader(
            GraphHeader.MAGIC_NUMBER,
            GraphHeader.CURRENT_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            1, // Node count (simplified for now)
            0, // Edge count (simplified for now)
            (uint)sourceBytes.Length,
            sourceTextOffset,
            intervalTreeOffset
        );

        // Write header at the beginning
        var headerBytes = StructToBytes(_header);
        for (int i = 0; i < headerBytes.Length; i++)
        {
            _buffer[i] = headerBytes[i];
        }

        // Create final buffer
        var finalBuffer = new CognitiveGraphBuffer(_buffer.ToArray(), takeOwnership: true);
        return finalBuffer;
    }

    /// <summary>
    /// Builds the final graph buffer in memory using V2 schema
    /// </summary>
    private CognitiveGraphBuffer BuildV2(uint rootNodeOffset, string sourceText)
    {
        // Write source text
        var sourceTextOffsetV2 = _currentOffsetV2;
        var sourceBytes = Encoding.UTF8.GetBytes(sourceText);
        _buffer.AddRange(sourceBytes);
        _currentOffsetV2 += (ulong)sourceBytes.Length;

        // Write interval tree index
        var intervalTreeOffsetV2 = _currentOffsetV2;
        var intervalTreeBytes = _intervalTree.Serialize();
        _buffer.AddRange(intervalTreeBytes);
        _currentOffsetV2 += (ulong)intervalTreeBytes.Length;

        // Create and write V2 header
        _headerV2 = new GraphHeaderV2(
            GraphHeaderV2.MAGIC_NUMBER,
            GraphHeaderV2.SCHEMA_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            1, // Node count (simplified for now)
            0, // Edge count (simplified for now)
            (ulong)sourceBytes.Length,
            sourceTextOffsetV2,
            intervalTreeOffsetV2
        );

        // Write header at the beginning
        var headerBytes = StructToBytes(_headerV2);
        for (int i = 0; i < headerBytes.Length; i++)
        {
            _buffer[i] = headerBytes[i];
        }

        // Create final buffer (wrapping as CognitiveGraphBuffer for compatibility)
        var finalBuffer = new CognitiveGraphBuffer(_buffer.ToArray(), takeOwnership: true);
        return finalBuffer;
    }

    /// <summary>
    /// Builds the final graph and writes directly to a file stream for large-scale persistence
    /// </summary>
    public void Build(FileStream stream, uint rootNodeOffset, string sourceText)
    {
        if (stream == null)
            throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite)
            throw new ArgumentException("Stream must be writable", nameof(stream));

        if (_options.Schema == SchemaVersion.V2)
        {
            BuildV2ToStream(stream, rootNodeOffset, sourceText);
            return;
        }

        // V1 implementation
        // Write source text to buffer
        var sourceTextOffset = _currentOffset;
        var sourceBytes = Encoding.UTF8.GetBytes(sourceText);
        _buffer.AddRange(sourceBytes);
        _currentOffset += (uint)sourceBytes.Length;

        // Write interval tree index to buffer
        var intervalTreeOffset = _currentOffset;
        var intervalTreeBytes = _intervalTree.Serialize();
        _buffer.AddRange(intervalTreeBytes);
        _currentOffset += (uint)intervalTreeBytes.Length;

        // Create header
        _header = new GraphHeader(
            GraphHeader.MAGIC_NUMBER,
            GraphHeader.CURRENT_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            1, // Node count (simplified for now)
            0, // Edge count (simplified for now)
            (uint)sourceBytes.Length,
            sourceTextOffset,
            intervalTreeOffset
        );

        // Write header at the beginning
        var headerBytes = StructToBytes(_header);
        for (int i = 0; i < headerBytes.Length; i++)
        {
            _buffer[i] = headerBytes[i];
        }

        // Write entire buffer to stream
        stream.Write(_buffer.ToArray());
        stream.Flush();
    }

    /// <summary>
    /// Builds V2 graph and writes to file stream
    /// </summary>
    private void BuildV2ToStream(FileStream stream, uint rootNodeOffset, string sourceText)
    {
        // Write source text
        var sourceTextOffsetV2 = _currentOffsetV2;
        var sourceBytes = Encoding.UTF8.GetBytes(sourceText);
        _buffer.AddRange(sourceBytes);
        _currentOffsetV2 += (ulong)sourceBytes.Length;

        // Write interval tree index
        var intervalTreeOffsetV2 = _currentOffsetV2;
        var intervalTreeBytes = _intervalTree.Serialize();
        _buffer.AddRange(intervalTreeBytes);
        _currentOffsetV2 += (ulong)intervalTreeBytes.Length;

        // Create and write V2 header
        _headerV2 = new GraphHeaderV2(
            GraphHeaderV2.MAGIC_NUMBER,
            GraphHeaderV2.SCHEMA_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            1, // Node count (simplified for now)
            0, // Edge count (simplified for now)
            (ulong)sourceBytes.Length,
            sourceTextOffsetV2,
            intervalTreeOffsetV2
        );

        // Write header at the beginning
        var headerBytes = StructToBytes(_headerV2);
        for (int i = 0; i < headerBytes.Length; i++)
        {
            _buffer[i] = headerBytes[i];
        }

        // Write entire buffer to stream
        stream.Write(_buffer.ToArray());
        stream.Flush();
    }

    private void WriteStruct<T>(T value) where T : unmanaged
    {
        var bytes = StructToBytes(value);
        _buffer.AddRange(bytes);
        
        if (_options.Schema == SchemaVersion.V1)
            _currentOffset += (uint)bytes.Length;
        else
            _currentOffsetV2 += (ulong)bytes.Length;
    }

    private static byte[] StructToBytes<T>(T value) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var bytes = new byte[size];
        
        unsafe
        {
            fixed (byte* ptr = bytes)
            {
                Marshal.StructureToPtr(value, (IntPtr)ptr, false);
            }
        }
        
        return bytes;
    }

    private static uint GetValueLength(PropertyValueType type, object value)
    {
        return type switch
        {
            PropertyValueType.String => (uint)Encoding.UTF8.GetByteCount((string)value),
            PropertyValueType.Int32 => sizeof(int),
            PropertyValueType.UInt32 => sizeof(uint),
            PropertyValueType.Boolean => sizeof(byte),
            PropertyValueType.Double => sizeof(double),
            _ => throw new ArgumentException($"Unsupported property value type: {type}")
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}