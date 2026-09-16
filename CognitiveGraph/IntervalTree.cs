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
/// <remarks>
/// Queries are answered through a centered interval tree index (built lazily on first
/// query, invalidated by <see cref="Add"/>) in O(log n + k) time, where k is the number
/// of matching intervals. The serialized layout is unchanged: a 32-bit node count
/// followed by the intervals sorted by <see cref="IntervalNode.Start"/>.
/// </remarks>
public sealed class IntervalTree
{
    private readonly List<IntervalNode> _nodes;
    private bool _isSorted;
    private CenteredIntervalIndex? _index;

    public IntervalTree()
    {
        _nodes = new List<IntervalNode>();
        _isSorted = true;
        _index = null;
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
        _index = null;
    }

    /// <summary>
    /// Finds all nodes that contain the specified byte offset.
    /// Runs in O(log n + k) via the centered interval index.
    /// </summary>
    public List<uint> FindNodesAt(uint byteOffset)
    {
        EnsureSorted();

        var result = new List<uint>();
        if (_nodes.Count == 0)
            return result;

        // The index is built once per tree instance (Add invalidates it). CognitiveGraph
        // caches the deserialized tree, so the build cost is amortized across all queries.
        _index ??= CenteredIntervalIndex.Build(_nodes);
        _index.FindNodesAt(byteOffset, result);

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
/// Centered interval tree for stabbing queries ("which intervals contain point p?").
/// Answers in O(log n + k); built in O(n log n) time and O(n) extra space.
/// </summary>
/// <remarks>
/// Each tree node picks a center point X (the Start of the median interval, so the
/// tree is balanced by construction). Intervals containing X are stored at the tree
/// node twice — sorted by Start ascending and by End descending — which lets a query
/// stop scanning as soon as an interval cannot contain the query point. Intervals
/// entirely left of X (End &lt; X) go to the left subtree; intervals entirely right of X
/// (Start &gt; X) go to the right subtree. Every interval is stored at exactly one
/// tree node, so total storage is O(n).
/// </remarks>
internal sealed class CenteredIntervalIndex
{
    private readonly IntervalNode[] _nodes;     // sorted by Start ascending
    private readonly uint[] _centers;           // center point X per tree node
    private readonly int[] _left;               // left child index per tree node (-1 = none)
    private readonly int[] _right;              // right child index per tree node (-1 = none)
    private readonly int[][] _byStart;          // per tree node: interval indices, Start ascending
    private readonly int[][] _byEndDescending;  // per tree node: interval indices, End descending

    private CenteredIntervalIndex(
        IntervalNode[] nodes,
        uint[] centers,
        int[] left,
        int[] right,
        int[][] byStart,
        int[][] byEndDescending)
    {
        _nodes = nodes;
        _centers = centers;
        _left = left;
        _right = right;
        _byStart = byStart;
        _byEndDescending = byEndDescending;
    }

    /// <summary>
    /// Builds a centered interval index over intervals sorted by Start.
    /// </summary>
    public static CenteredIntervalIndex Build(IReadOnlyList<IntervalNode> sortedNodes)
    {
        var nodes = new IntervalNode[sortedNodes.Count];
        for (int i = 0; i < sortedNodes.Count; i++)
            nodes[i] = sortedNodes[i];

        var indices = new int[nodes.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        var centers = new List<uint>();
        var left = new List<int>();
        var right = new List<int>();
        var byStart = new List<int[]>();
        var byEndDescending = new List<int[]>();

        BuildNode(nodes, indices, centers, left, right, byStart, byEndDescending);

        return new CenteredIntervalIndex(
            nodes,
            centers.ToArray(),
            left.ToArray(),
            right.ToArray(),
            byStart.ToArray(),
            byEndDescending.ToArray());
    }

    /// <summary>
    /// Recursively builds the tree over <paramref name="indices"/> (sorted by Start).
    /// Returns the tree-node index, or -1 for an empty range.
    /// </summary>
    private static int BuildNode(
        IntervalNode[] nodes,
        int[] indices,
        List<uint> centers,
        List<int> left,
        List<int> right,
        List<int[]> byStart,
        List<int[]> byEndDescending)
    {
        if (indices.Length == 0)
            return -1;

        // Median Start keeps the tree balanced: both child ranges are at most half the size.
        var center = nodes[indices[indices.Length / 2]].Start;

        var atNode = new List<int>();
        var leftIndices = new List<int>();
        var rightIndices = new List<int>();

        foreach (var i in indices)
        {
            // Indices are sorted by Start, so the three filters below preserve that order
            // in each partition and keep `atNode` sorted by Start ascending.
            if (nodes[i].End < center)
                leftIndices.Add(i);
            else if (nodes[i].Start > center)
                rightIndices.Add(i);
            else
                atNode.Add(i); // contains center: Start <= center <= End
        }

        var treeNodeIndex = centers.Count;
        centers.Add(center);
        byStart.Add(atNode.ToArray());

        // Same intervals at this tree node, ordered by End descending for queries right of center.
        var endDescending = new List<int>(atNode);
        endDescending.Sort((a, b) => nodes[b].End.CompareTo(nodes[a].End));
        byEndDescending.Add(endDescending.ToArray());

        // Reserve this node's child slots before recursing: the recursive calls append
        // their own entries to the same lists, so the slots must already be claimed.
        left.Add(-1);
        right.Add(-1);

        left[treeNodeIndex] = BuildNode(nodes, leftIndices.ToArray(), centers, left, right, byStart, byEndDescending);
        right[treeNodeIndex] = BuildNode(nodes, rightIndices.ToArray(), centers, left, right, byStart, byEndDescending);

        return treeNodeIndex;
    }

    /// <summary>
    /// Adds all node offsets whose interval contains <paramref name="point"/> to
    /// <paramref name="result"/>. Iterative walk, O(log n + k).
    /// </summary>
    public void FindNodesAt(uint point, List<uint> result)
    {
        var node = 0;
        while (node >= 0)
        {
            var center = _centers[node];

            if (point == center)
            {
                // Every interval at this tree node contains the center, hence the point.
                foreach (var i in _byStart[node])
                    result.Add(_nodes[i].NodeOffset);
                return; // subtrees lie strictly left/right of center; cannot contain it
            }

            if (point < center)
            {
                // Intervals at this node all contain center > point, so they contain the
                // point exactly when Start <= point. Sorted by Start: stop at first miss.
                foreach (var i in _byStart[node])
                {
                    if (_nodes[i].Start > point)
                        break;
                    result.Add(_nodes[i].NodeOffset);
                }

                node = _left[node];
            }
            else // point > center
            {
                // Intervals at this node all contain center < point, so they contain the
                // point exactly when End >= point. Sorted by End desc: stop at first miss.
                foreach (var i in _byEndDescending[node])
                {
                    if (_nodes[i].End < point)
                        break;
                    result.Add(_nodes[i].NodeOffset);
                }

                node = _right[node];
            }
        }
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
