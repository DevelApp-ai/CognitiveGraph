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
using System.Runtime.InteropServices;
using CognitiveGraph.Buffer;

namespace CognitiveGraph;

/// <summary>
/// High-performance interval tree for spatial querying of source code locations.
/// Stores intervals in a serializable format for efficient range queries.
/// </summary>
public sealed class IntervalTree
{
    private readonly List<IntervalNode> _nodes;
    private bool _isSorted;

    public IntervalTree()
    {
        _nodes = new List<IntervalNode>();
        _isSorted = true;
    }

    /// <summary>
    /// Adds an interval to the tree
    /// </summary>
    public void Add(uint start, uint end, uint nodeOffset)
    {
        if (start > end)
            throw new ArgumentException("Start must be less than or equal to end");

        _nodes.Add(new IntervalNode(start, end, nodeOffset));
        _isSorted = false;
    }

    /// <summary>
    /// Finds all nodes that contain the specified byte offset
    /// </summary>
    public List<uint> FindNodesAt(uint byteOffset)
    {
        EnsureSorted();
        
        var result = new List<uint>();
        
        foreach (var node in _nodes)
        {
            if (node.Start <= byteOffset && byteOffset <= node.End)
            {
                result.Add(node.NodeOffset);
            }
        }
        
        return result;
    }

    /// <summary>
    /// Serializes the interval tree to a byte array for storage in the graph buffer
    /// </summary>
    public byte[] Serialize()
    {
        EnsureSorted();
        
        var bufferSize = sizeof(uint) + (_nodes.Count * IntervalNode.SIZE);
        var buffer = new byte[bufferSize];
        var offset = 0;

        // Write count
        BitConverter.TryWriteBytes(buffer.AsSpan(offset), (uint)_nodes.Count);
        offset += sizeof(uint);

        // Write nodes
        foreach (var node in _nodes)
        {
            var nodeBytes = StructToBytes(node);
            nodeBytes.CopyTo(buffer.AsSpan(offset));
            offset += nodeBytes.Length;
        }

        return buffer;
    }

    /// <summary>
    /// Deserializes an interval tree from a buffer
    /// </summary>
    public static IntervalTree Deserialize(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(uint))
            throw new ArgumentException("Buffer too small for interval tree");

        var tree = new IntervalTree();
        var offset = 0;

        // Read count
        var count = BitConverter.ToUInt32(buffer.Slice(offset));
        offset += sizeof(uint);

        // Read nodes
        for (int i = 0; i < count; i++)
        {
            if (offset + IntervalNode.SIZE > buffer.Length)
                throw new ArgumentException("Buffer too small for interval tree nodes");

            var nodeBytes = buffer.Slice(offset, IntervalNode.SIZE);
            var node = MemoryMarshal.Read<IntervalNode>(nodeBytes);
            tree._nodes.Add(node);
            offset += IntervalNode.SIZE;
        }

        tree._isSorted = true;
        return tree;
    }

    private void EnsureSorted()
    {
        if (!_isSorted)
        {
            _nodes.Sort((a, b) => a.Start.CompareTo(b.Start));
            _isSorted = true;
        }
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

    /// <summary>
    /// Gets the size of the serialized interval tree
    /// </summary>
    public uint GetSerializedSize()
    {
        return sizeof(uint) + ((uint)_nodes.Count * IntervalNode.SIZE);
    }
}

/// <summary>
/// Binary layout for interval tree nodes
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct IntervalNode
{
    /// <summary>
    /// Start byte offset in source code
    /// </summary>
    public readonly uint Start;
    
    /// <summary>
    /// End byte offset in source code
    /// </summary>
    public readonly uint End;
    
    /// <summary>
    /// Offset to the symbol node in the graph buffer
    /// </summary>
    public readonly uint NodeOffset;

    public IntervalNode(uint start, uint end, uint nodeOffset)
    {
        Start = start;
        End = end;
        NodeOffset = nodeOffset;
    }

    /// <summary>
    /// Size of the interval node in bytes
    /// </summary>
    public const int SIZE = 12; // 3 * sizeof(uint)
}