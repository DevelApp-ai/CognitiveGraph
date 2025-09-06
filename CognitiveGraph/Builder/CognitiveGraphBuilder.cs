using System;
using System.Buffers;
using System.Collections.Generic;
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
    private uint _currentOffset;
    private GraphHeader _header;
    private bool _disposed;

    public CognitiveGraphBuilder()
    {
        _buffer = new List<byte>();
        _stringTable = new Dictionary<string, uint>();
        _currentOffset = 0;

        // Reserve space for header (will be written last)
        _buffer.AddRange(new byte[GraphHeader.SIZE]);
        _currentOffset = GraphHeader.SIZE;
    }

    /// <summary>
    /// Writes a string to the buffer and returns its offset
    /// </summary>
    public uint WriteString(string value)
    {
        if (_stringTable.TryGetValue(value, out var existingOffset))
            return existingOffset;

        var offset = _currentOffset;
        var bytes = Encoding.UTF8.GetBytes(value);
        
        _buffer.AddRange(bytes);
        _buffer.Add(0); // null terminator
        _currentOffset += (uint)(bytes.Length + 1);
        
        _stringTable[value] = offset;
        return offset;
    }

    /// <summary>
    /// Writes a property value to the buffer
    /// </summary>
    public uint WritePropertyValue(PropertyValueType type, object value)
    {
        var offset = _currentOffset;
        
        // Write header
        var header = new PropertyValueHeader(type, GetValueLength(type, value));
        WriteStruct(header);

        // Write value data
        switch (type)
        {
            case PropertyValueType.String:
                var stringBytes = Encoding.UTF8.GetBytes((string)value);
                _buffer.AddRange(stringBytes);
                _currentOffset += (uint)stringBytes.Length;
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
    /// Writes a symbol node to the buffer
    /// </summary>
    public uint WriteSymbolNode(ushort symbolId, ushort nodeType, uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null, IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
    {
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
                propertyDataList.Add(new PropertyData(keyOffset, valueOffset));
            }
            
            propertiesOffset = WriteList(propertyDataList, p => { WriteStruct(p); return 0; });
        }

        // Now write the symbol node data and capture its offset
        var nodeOffset = _currentOffset;
        var nodeData = new SymbolNodeData(symbolId, nodeType, sourceStart, sourceLength, packedNodesOffset, propertiesOffset);
        WriteStruct(nodeData);
        
        return nodeOffset;
    }

    /// <summary>
    /// Writes a packed node to the buffer
    /// </summary>
    public uint WritePackedNode(ushort ruleId, IReadOnlyList<uint>? childNodeOffsets = null, IReadOnlyList<CpgEdgeData>? cpgEdges = null)
    {
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
    /// Builds the final graph buffer
    /// </summary>
    public CognitiveGraphBuffer Build(uint rootNodeOffset, string sourceText)
    {
        // Write source text
        var sourceTextOffset = _currentOffset;
        var sourceBytes = Encoding.UTF8.GetBytes(sourceText);
        _buffer.AddRange(sourceBytes);

        // Create and write header
        _header = new GraphHeader(
            GraphHeader.MAGIC_NUMBER,
            GraphHeader.CURRENT_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            1, // Node count (simplified for now)
            0, // Edge count (simplified for now)
            (uint)sourceBytes.Length,
            sourceTextOffset
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

    private void WriteStruct<T>(T value) where T : unmanaged
    {
        var bytes = StructToBytes(value);
        _buffer.AddRange(bytes);
        _currentOffset += (uint)bytes.Length;
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