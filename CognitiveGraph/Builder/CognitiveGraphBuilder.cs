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
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Builder;

/// <summary>
/// Builder for constructing Cognitive Graphs incrementally.
/// Uses a resizable buffer approach for dynamic construction.
/// </summary>
/// <remarks>
/// The hot-path write methods (<see cref="WriteSymbolNode"/>, <see cref="WritePackedNode"/>,
/// <see cref="WritePropertyList"/>, <see cref="WriteString"/>, <see cref="WritePropertyValue"/>)
/// are allocation-free apart from the amortized growth of the backing byte buffer: structs are
/// written directly into the buffer with <see cref="MemoryMarshal.Write"/>, strings are UTF-8
/// encoded straight into the buffer, and no intermediate lists, arrays, boxes or delegates are
/// created per call. This matters because parsers call <see cref="WriteSymbolNode"/> for every
/// shifted token and every reduction, across all concurrent GLR paths (see issue #40).
/// </remarks>
public sealed class CognitiveGraphBuilder : IDisposable
{
    private byte[] _buffer;
    private int _length;
    private readonly Dictionary<string, ulong> _stringTable;
    private readonly IntervalTree _intervalTree;
    private ulong _symbolNodeCount;
    private ulong _packedNodeCount;
    private ulong _cpgEdgeCount;
    private GraphHeader _header;
    private GraphHeaderV2 _headerV2;
    private readonly GraphBuilderOptions _options;
    private bool _disposed;

    /// <summary>
    /// Current V1 write offset — always the number of bytes written so far.
    /// </summary>
    private uint CurrentOffsetV1 => (uint)_length;

    /// <summary>
    /// Current V2 write offset — always the number of bytes written so far.
    /// </summary>
    private ulong CurrentOffsetV2 => (ulong)_length;

    public CognitiveGraphBuilder() : this(new GraphBuilderOptions())
    {
    }

    public CognitiveGraphBuilder(GraphBuilderOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stringTable = new Dictionary<string, ulong>();
        _intervalTree = new IntervalTree();

        // Reserve space for the header (written last). A fresh byte[] is already zeroed,
        // so unlike the previous List<byte> implementation this reserves no temp array.
        var headerSize = _options.Schema == SchemaVersion.V1 ? GraphHeader.SIZE : GraphHeaderV2.SIZE;
        _buffer = new byte[Math.Max(_options.InitialCapacity, headerSize)];
        _length = headerSize;
    }

    /// <summary>
    /// Writes a string to the buffer and returns its offset. Strings are deduplicated
    /// through an internal table, so repeated keys are written (and encoded) only once.
    /// </summary>
    public ulong WriteString(string value)
    {
        if (_stringTable.TryGetValue(value, out var existingOffset))
            return existingOffset;

        ulong offset = _options.Schema == SchemaVersion.V1 ? CurrentOffsetV1 : CurrentOffsetV2;

        // Encode UTF-8 directly into the buffer — no intermediate byte[].
        var byteCount = Encoding.UTF8.GetByteCount(value);
        EnsureCapacity(byteCount + 1);
        Encoding.UTF8.GetBytes(value.AsSpan(), _buffer.AsSpan(_length, byteCount));
        _length += byteCount;
        _buffer[_length++] = 0; // null terminator

        _stringTable[value] = offset;
        return offset;
    }

    /// <summary>
    /// Writes a property value to the buffer
    /// </summary>
    public ulong WritePropertyValue(PropertyValueType type, object value)
    {
        ulong offset = _options.Schema == SchemaVersion.V1 ? CurrentOffsetV1 : CurrentOffsetV2;

        // Write header
        AppendStruct(new PropertyValueHeader(type, GetValueLength(type, value)));

        // Write value data
        switch (type)
        {
            case PropertyValueType.String:
                var s = (string)value;
                var byteCount = Encoding.UTF8.GetByteCount(s);
                EnsureCapacity(byteCount);
                Encoding.UTF8.GetBytes(s.AsSpan(), _buffer.AsSpan(_length, byteCount));
                _length += byteCount;
                break;

            case PropertyValueType.Int32:
                AppendStruct((int)value);
                break;

            case PropertyValueType.UInt32:
                AppendStruct((uint)value);
                break;

            case PropertyValueType.Boolean:
                EnsureCapacity(1);
                _buffer[_length++] = (byte)((bool)value ? 1 : 0);
                break;

            case PropertyValueType.Double:
                AppendStruct((double)value);
                break;

            case PropertyValueType.Int64:
                AppendStruct((long)value);
                break;

            case PropertyValueType.UInt64:
                AppendStruct((ulong)value);
                break;

            case PropertyValueType.Float:
                AppendStruct((float)value);
                break;

            case PropertyValueType.Binary:
                var binaryBytes = (byte[])value;
                Append(binaryBytes);
                break;

            default:
                throw new ArgumentException($"Unsupported property value type: {type}");
        }

        return offset;
    }

    /// <summary>
    /// Writes a list of items to the buffer. The item writer's return value is ignored;
    /// the returned offset points at the list's item count.
    /// </summary>
    public uint WriteList<T>(IReadOnlyList<T> items, Func<T, uint> itemWriter)
    {
        var offset = CurrentOffsetV1;

        // Write count
        AppendStruct((uint)items.Count);

        // Write items
        for (int i = 0; i < items.Count; i++)
        {
            itemWriter(items[i]);
        }

        return offset;
    }

    /// <summary>
    /// Writes a list of properties to the buffer and returns the offset of the list.
    /// Used by symbol nodes, CPG edges and other property-bearing records.
    /// Layout: [count][PropertyData slots (contiguous)][key/value data] — the slots are
    /// reserved up front and patched in place once the key/value offsets are known, so
    /// readers keep their contiguous [count][PropertyData...] view without the builder
    /// allocating an intermediate list.
    /// </summary>
    public ulong WritePropertyList(IReadOnlyList<(string key, PropertyValueType type, object value)>? properties)
    {
        if (properties == null || properties.Count == 0)
            return 0;

        if (_options.Schema == SchemaVersion.V2)
        {
            var listOffset = CurrentOffsetV2;
            AppendStruct((ulong)properties.Count);

            // Reserve contiguous PropertyDataV2 slots (zero-filled placeholders)
            var slotsOffset = _length;
            for (int i = 0; i < properties.Count; i++)
                AppendStruct(default(PropertyDataV2));

            for (int i = 0; i < properties.Count; i++)
            {
                var (key, type, value) = properties[i];
                var keyOffset = WriteString(key);
                var valueOffset = WritePropertyValue(type, value);
                WriteStructAt(slotsOffset + i * PropertyDataV2.SIZE, new PropertyDataV2(keyOffset, valueOffset));
            }
            return listOffset;
        }
        else
        {
            var listOffset = CurrentOffsetV1;
            AppendStruct((uint)properties.Count);

            // Reserve contiguous PropertyData slots (zero-filled placeholders)
            var slotsOffset = _length;
            for (int i = 0; i < properties.Count; i++)
                AppendStruct(default(PropertyData));

            for (int i = 0; i < properties.Count; i++)
            {
                var (key, type, value) = properties[i];
                var keyOffset = WriteString(key);
                var valueOffset = WritePropertyValue(type, value);
                // PropertyData uses uint offsets (V1 schema), cast for compatibility
                WriteStructAt(slotsOffset + i * PropertyData.SIZE, new PropertyData((uint)keyOffset, (uint)valueOffset));
            }
            return listOffset;
        }
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
        var packedNodesOffset = packedNodeOffsets is { Count: > 0 }
            ? WriteStructList(packedNodeOffsets)
            : 0u;

        // Write properties list
        var propertiesOffset = properties is { Count: > 0 }
            ? (uint)WritePropertyList(properties)
            : 0u;

        // Now write the symbol node data and capture its offset
        var nodeOffset = CurrentOffsetV1;
        var nodeData = new SymbolNodeData(symbolId, nodeType, sourceStart, sourceLength, packedNodesOffset, propertiesOffset);
        AppendStruct(nodeData);

        // Add to interval tree for spatial indexing
        _intervalTree.Add(sourceStart, sourceStart + sourceLength - 1, nodeOffset);
        _symbolNodeCount++;

        return nodeOffset;
    }

    /// <summary>
    /// Writes a symbol node to the buffer using V2 schema
    /// </summary>
    private ulong WriteSymbolNodeV2(uint symbolId, uint nodeType, uint sourceStart, uint sourceLength,
        IReadOnlyList<uint>? packedNodeOffsets = null, IReadOnlyList<(string key, PropertyValueType type, object value)>? properties = null)
    {
        // Write packed nodes list with 64-bit offsets
        var packedNodesOffsetV2 = packedNodeOffsets is { Count: > 0 }
            ? WriteStructListV2(packedNodeOffsets)
            : 0UL;

        // Write properties list
        var propertiesOffsetV2 = properties is { Count: > 0 }
            ? WritePropertyList(properties)
            : 0UL;

        // Now write the symbol node data and capture its offset
        var nodeOffset = CurrentOffsetV2;
        var nodeData = new SymbolNodeDataV2(symbolId, nodeType, sourceStart, sourceLength, packedNodesOffsetV2, propertiesOffsetV2);
        AppendStruct(nodeData);

        // Add to interval tree for spatial indexing
        _intervalTree.Add(sourceStart, sourceStart + sourceLength - 1, (uint)nodeOffset);
        _symbolNodeCount++;

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
        var childNodesOffset = childNodeOffsets is { Count: > 0 }
            ? WriteStructList(childNodeOffsets)
            : 0u;

        // Write CPG edges list
        var cpgEdgesOffset = 0u;
        if (cpgEdges is { Count: > 0 })
        {
            _cpgEdgeCount += (ulong)cpgEdges.Count;
            cpgEdgesOffset = WriteStructList(cpgEdges);
        }

        // Now write the packed node data and capture its offset
        var nodeOffset = CurrentOffsetV1;
        var nodeData = new PackedNodeData(ruleId, childNodesOffset, cpgEdgesOffset);
        AppendStruct(nodeData);
        _packedNodeCount++;

        return nodeOffset;
    }

    /// <summary>
    /// Writes a packed node to the buffer using V2 schema
    /// </summary>
    private ulong WritePackedNodeV2(uint ruleId, IReadOnlyList<uint>? childNodeOffsets = null, IReadOnlyList<CpgEdgeData>? cpgEdges = null)
    {
        // Write child nodes list with 64-bit offsets
        var childNodesOffsetV2 = childNodeOffsets is { Count: > 0 }
            ? WriteStructListV2(childNodeOffsets)
            : 0UL;

        // Write CPG edges list
        var cpgEdgesOffsetV2 = 0UL;
        if (cpgEdges is { Count: > 0 })
        {
            _cpgEdgeCount += (ulong)cpgEdges.Count;
            cpgEdgesOffsetV2 = WriteStructListV2(cpgEdges);
        }

        // Now write the packed node data and capture its offset
        var nodeOffset = CurrentOffsetV2;
        var nodeData = new PackedNodeDataV2(ruleId, childNodesOffsetV2, cpgEdgesOffsetV2);
        AppendStruct(nodeData);
        _packedNodeCount++;

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
        var sourceTextOffset = CurrentOffsetV1;
        Append(Encoding.UTF8.GetBytes(sourceText));

        // Write interval tree index
        var intervalTreeOffset = CurrentOffsetV1;
        Append(_intervalTree.Serialize());

        // Create and write header
        _header = new GraphHeader(
            GraphHeader.MAGIC_NUMBER,
            GraphHeader.CURRENT_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            (uint)_symbolNodeCount,  // Total symbol nodes written
            (uint)_cpgEdgeCount,     // Total CPG edges written
            (uint)Encoding.UTF8.GetByteCount(sourceText),
            sourceTextOffset,
            intervalTreeOffset
        );

        // Write header at the beginning
        WriteStructAt(0, _header);

        // Create final buffer (exact-size copy; the builder cannot be reused after Build)
        return new CognitiveGraphBuffer(ToArray(), takeOwnership: true);
    }

    /// <summary>
    /// Builds the final graph buffer in memory using V2 schema
    /// </summary>
    private CognitiveGraphBuffer BuildV2(uint rootNodeOffset, string sourceText)
    {
        // Write source text
        var sourceTextOffsetV2 = CurrentOffsetV2;
        Append(Encoding.UTF8.GetBytes(sourceText));

        // Write interval tree index
        var intervalTreeOffsetV2 = CurrentOffsetV2;
        Append(_intervalTree.Serialize());

        // Create and write V2 header
        _headerV2 = new GraphHeaderV2(
            GraphHeaderV2.MAGIC_NUMBER,
            GraphHeaderV2.SCHEMA_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            _symbolNodeCount, // Total symbol nodes written
            _cpgEdgeCount,    // Total CPG edges written
            (ulong)Encoding.UTF8.GetByteCount(sourceText),
            sourceTextOffsetV2,
            intervalTreeOffsetV2
        );

        // Write header at the beginning
        WriteStructAt(0, _headerV2);

        // Create final buffer (wrapping as CognitiveGraphBuffer for compatibility)
        return new CognitiveGraphBuffer(ToArray(), takeOwnership: true);
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
        var sourceTextOffset = CurrentOffsetV1;
        Append(Encoding.UTF8.GetBytes(sourceText));

        // Write interval tree index to buffer
        var intervalTreeOffset = CurrentOffsetV1;
        Append(_intervalTree.Serialize());

        // Create header
        _header = new GraphHeader(
            GraphHeader.MAGIC_NUMBER,
            GraphHeader.CURRENT_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            (uint)_symbolNodeCount,  // Total symbol nodes written
            (uint)_cpgEdgeCount,     // Total CPG edges written
            (uint)Encoding.UTF8.GetByteCount(sourceText),
            sourceTextOffset,
            intervalTreeOffset
        );

        // Write header at the beginning
        WriteStructAt(0, _header);

        // Write the used portion of the buffer directly — no full-size ToArray() copy
        stream.Write(_buffer, 0, _length);
        stream.Flush();
    }

    /// <summary>
    /// Builds V2 graph and writes to file stream
    /// </summary>
    private void BuildV2ToStream(FileStream stream, uint rootNodeOffset, string sourceText)
    {
        // Write source text
        var sourceTextOffsetV2 = CurrentOffsetV2;
        Append(Encoding.UTF8.GetBytes(sourceText));

        // Write interval tree index
        var intervalTreeOffsetV2 = CurrentOffsetV2;
        Append(_intervalTree.Serialize());

        // Create and write V2 header
        _headerV2 = new GraphHeaderV2(
            GraphHeaderV2.MAGIC_NUMBER,
            GraphHeaderV2.SCHEMA_VERSION,
            (ushort)GraphFlags.FullyParsed,
            rootNodeOffset,
            _symbolNodeCount, // Total symbol nodes written
            _cpgEdgeCount,    // Total CPG edges written
            (ulong)Encoding.UTF8.GetByteCount(sourceText),
            sourceTextOffsetV2,
            intervalTreeOffsetV2
        );

        // Write header at the beginning
        WriteStructAt(0, _headerV2);

        // Write the used portion of the buffer directly — no full-size ToArray() copy
        stream.Write(_buffer, 0, _length);
        stream.Flush();
    }

    /// <summary>
    /// Writes a list of blittable structs: [count][item structs...]. V1 (32-bit count) layout.
    /// Allocation-free: items are written directly from the source list.
    /// </summary>
    private uint WriteStructList<T>(IReadOnlyList<T> items) where T : unmanaged
    {
        var offset = CurrentOffsetV1;
        AppendStruct((uint)items.Count);
        for (int i = 0; i < items.Count; i++)
            AppendStruct(items[i]);
        return offset;
    }

    /// <summary>
    /// Writes a list of blittable structs: [count][item structs...]. V2 (64-bit count) layout.
    /// Allocation-free: items are written directly from the source list.
    /// </summary>
    private ulong WriteStructListV2<T>(IReadOnlyList<T> items) where T : unmanaged
    {
        var offset = CurrentOffsetV2;
        AppendStruct((ulong)items.Count);
        for (int i = 0; i < items.Count; i++)
            AppendStruct(items[i]);
        return offset;
    }

    /// <summary>
    /// Appends a blittable struct at the current position. Allocation-free.
    /// </summary>
    private void AppendStruct<T>(T value) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();
        EnsureCapacity(size);
        MemoryMarshal.Write(_buffer.AsSpan(_length), ref value);
        _length += size;
    }

    /// <summary>
    /// Overwrites a blittable struct at a fixed position (used for header patching).
    /// </summary>
    private void WriteStructAt<T>(int position, T value) where T : unmanaged
    {
        MemoryMarshal.Write(_buffer.AsSpan(position, Unsafe.SizeOf<T>()), ref value);
    }

    /// <summary>
    /// Appends raw bytes at the current position. Allocation-free.
    /// </summary>
    private void Append(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_length));
        _length += data.Length;
    }

    /// <summary>
    /// Grows the backing buffer (amortized doubling) when needed.
    /// This is the only allocation on the builder hot path.
    /// </summary>
    private void EnsureCapacity(int additionalBytes)
    {
        var required = _length + additionalBytes;
        if (required <= _buffer.Length)
            return;

        var newSize = _buffer.Length * 2;
        while (newSize < required)
            newSize *= 2;
        Array.Resize(ref _buffer, newSize);
    }

    /// <summary>
    /// Returns an exact-size copy of the written bytes.
    /// </summary>
    private byte[] ToArray()
    {
        var result = new byte[_length];
        System.Buffer.BlockCopy(_buffer, 0, result, 0, _length);
        return result;
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
            PropertyValueType.Int64 => sizeof(long),
            PropertyValueType.UInt64 => sizeof(ulong),
            PropertyValueType.Float => sizeof(float),
            PropertyValueType.Binary => (uint)((byte[])value).Length,
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
