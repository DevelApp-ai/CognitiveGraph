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
    private CenteredIntervalNode? _root;

    public IntervalTree()
    {
        _nodes = new List<IntervalNode>();
        _isSorted = true;
        _root = null;
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
        _root = null;
    }

    /// <summary>
    /// Finds all node offsets whose intervals contain the specified byte offset (bounds inclusive).
    /// Runs in O(log n + k) where k is the number of matches, using a centered interval tree.
    /// Results are ordered by interval start offset.
    /// </summary>
    public List<uint> FindNodesAt(uint byteOffset)
    {
        EnsureSorted();
        var root = EnsureIndexed();

        var result = new List<uint>();
        if (root == null)
            return result;

        var matches = new List<IntervalNode>();
        Query(root, byteOffset, matches);

        // Preserve the historical result ordering (ascending interval start)
        matches.Sort((a, b) => a.Start.CompareTo(b.Start));
        foreach (var match in matches)
        {
            result.Add(match.NodeOffset);
        }

        return result;
    }

    /// <summary>
    /// Builds the centered interval tree query index on first use.
    /// The node list is not mutated between queries after deserialization, so the index
    /// is built at most once per instance. Benign race under concurrent first callers:
    /// reference assignment is atomic and the losing caller's index is garbage-collected.
    /// </summary>
    private CenteredIntervalNode? EnsureIndexed()
    {
        var root = _root;
        if (root == null)
        {
            root = Build(_nodes.ToArray(), 0, _nodes.Count);
            _root = root;
        }
        return root;
    }

    /// <summary>
    /// Recursively builds a centered interval tree over the given interval slice.
    /// The center is the median of all interval endpoints in the slice, which bounds
    /// each child to at most half of the slice, giving O(log n) tree depth.
    /// </summary>
    private static CenteredIntervalNode? Build(IntervalNode[] intervals, int offset, int count)
    {
        if (count == 0)
            return null;

        // Center = median of all 2*count endpoints in this slice
        var endpoints = new uint[count * 2];
        for (int i = 0; i < count; i++)
        {
            endpoints[i * 2] = intervals[offset + i].Start;
            endpoints[i * 2 + 1] = intervals[offset + i].End;
        }
        Array.Sort(endpoints);
        var center = endpoints[endpoints.Length / 2];

        var centeredCount = 0;
        var leftCount = 0;
        var rightCount = 0;

        // First pass: count the three partitions (intervals containing the center,
        // entirely left of it, entirely right of it)
        for (int i = 0; i < count; i++)
        {
            var interval = intervals[offset + i];
            if (interval.End < center) leftCount++;
            else if (interval.Start > center) rightCount++;
            else centeredCount++;
        }

        var node = new CenteredIntervalNode(center, centeredCount);
        var centered = node.ByStart;
        var left = new IntervalNode[leftCount];
        var right = new IntervalNode[rightCount];
        var centeredIdx = 0;
        var leftIdx = 0;
        var rightIdx = 0;

        // Second pass: distribute the intervals
        for (int i = 0; i < count; i++)
        {
            var interval = intervals[offset + i];
            if (interval.End < center) left[leftIdx++] = interval;
            else if (interval.Start > center) right[rightIdx++] = interval;
            else centered[centeredIdx++] = interval;
        }

        // ByStart: intervals containing the center, sorted by start (ascending)
        Array.Sort(centered, (a, b) => a.Start.CompareTo(b.Start));
        // ByEnd: same intervals, sorted by end (descending)
        var byEnd = (IntervalNode[])centered.Clone();
        Array.Sort(byEnd, (a, b) => b.End.CompareTo(a.End));
        node.ByEnd = byEnd;

        node.Left = Build(left, 0, left.Length);
        node.Right = Build(right, 0, right.Length);
        return node;
    }

    /// <summary>
    /// Queries the centered interval tree iteratively. At each visited node:
    /// - p &lt; center: intervals with Start &lt;= p all contain p (their End &gt;= center &gt; p),
    ///   so scan ByStart in ascending order and stop at the first Start &gt; p, then go left.
    /// - p &gt; center: intervals with End &gt;= p all contain p (their Start &lt;= center &lt; p),
    ///   so scan ByEnd in descending order and stop at the first End &lt; p, then go right.
    /// - p == center: every interval stored at the node contains p.
    /// Total work is O(depth + matches) = O(log n + k).
    /// </summary>
    private static void Query(CenteredIntervalNode node, uint byteOffset, List<IntervalNode> result)
    {
        while (true)
        {
            if (byteOffset < node.Center)
            {
                var byStart = node.ByStart;
                for (int i = 0; i < byStart.Length; i++)
                {
                    if (byStart[i].Start > byteOffset)
                        break;
                    result.Add(byStart[i]);
                }
                if (node.Left == null)
                    return;
                node = node.Left;
            }
            else if (byteOffset > node.Center)
            {
                var byEnd = node.ByEnd;
                for (int i = 0; i < byEnd.Length; i++)
                {
                    if (byEnd[i].End < byteOffset)
                        break;
                    result.Add(byEnd[i]);
                }
                if (node.Right == null)
                    return;
                node = node.Right;
            }
            else
            {
                result.AddRange(node.ByStart);
                return;
            }
        }
    }

    /// <summary>
    /// A node of the centered interval tree. Stores the intervals that straddle its
    /// center point in two orderings for early-exit scans, plus the two child subtrees.
    /// </summary>
    private sealed class CenteredIntervalNode
    {
        public readonly uint Center;
        public readonly IntervalNode[] ByStart;
        public IntervalNode[] ByEnd = Array.Empty<IntervalNode>();
        public CenteredIntervalNode? Left;
        public CenteredIntervalNode? Right;

        public CenteredIntervalNode(uint center, int centeredCount)
        {
            Center = center;
            ByStart = new IntervalNode[centeredCount];
        }
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