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
using System.Diagnostics;
using Xunit;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for the centered interval tree query index (issue #22):
/// correctness at scale and O(log n + k) query behavior.
/// </summary>
public class IntervalTreeQueryTests
{
    private static List<(uint Start, uint End, uint Offset)> GenerateIntervals(int count, int seed, uint maxStart, uint maxLength)
    {
        var rng = new Random(seed);
        var intervals = new List<(uint, uint, uint)>(count);
        for (int i = 0; i < count; i++)
        {
            var start = (uint)rng.Next((int)maxStart);
            var length = (uint)rng.Next(1, (int)maxLength + 1);
            intervals.Add((start, start + length, (uint)(i + 1)));
        }
        return intervals;
    }

    private static IntervalTree BuildTree(IEnumerable<(uint Start, uint End, uint Offset)> intervals)
    {
        var tree = new IntervalTree();
        foreach (var (start, end, offset) in intervals)
        {
            tree.Add(start, end, offset);
        }
        return tree;
    }

    private static List<uint> BruteForce(IEnumerable<(uint Start, uint End, uint Offset)> intervals, uint point)
    {
        var result = new List<uint>();
        foreach (var (start, end, offset) in intervals)
        {
            if (start <= point && point <= end)
            {
                result.Add(offset);
            }
        }
        result.Sort();
        return result;
    }

    [Fact]
    public void FindNodesAt_LargeRandomTree_MatchesBruteForce()
    {
        // Arrange
        const int Count = 5000;
        var intervals = GenerateIntervals(Count, seed: 42, maxStart: 100_000, maxLength: 200);
        var tree = BuildTree(intervals);

        // Act & Assert - query random points, boundary points and far-out points
        var rng = new Random(7);
        for (int q = 0; q < 500; q++)
        {
            uint point = (uint)rng.Next(0, 102_000);
            AssertEqualResults(intervals, tree, point);
        }

        foreach (var (start, end, _) in intervals.GetRange(0, 50))
        {
            AssertEqualResults(intervals, tree, start);      // inclusive lower bound
            AssertEqualResults(intervals, tree, end);        // inclusive upper bound
            AssertEqualResults(intervals, tree, start + 1);  // just inside
            if (start > 0)
                AssertEqualResults(intervals, tree, start - 1); // just outside
        }

        AssertEqualResults(intervals, tree, uint.MaxValue);
    }

    [Fact]
    public void FindNodesAt_IdenticalIntervals_ReturnsAll()
    {
        // Arrange - worst case for partitioning: many identical intervals
        var intervals = new List<(uint, uint, uint)>();
        for (uint i = 0; i < 1000; i++)
        {
            intervals.Add((100, 200, i + 1));
        }
        var tree = BuildTree(intervals);

        // Act
        var atStart = tree.FindNodesAt(100);
        var atMiddle = tree.FindNodesAt(150);
        var atEnd = tree.FindNodesAt(200);
        var outside = tree.FindNodesAt(99);
        var outside2 = tree.FindNodesAt(201);

        // Assert
        Assert.Equal(1000, atStart.Count);
        Assert.Equal(1000, atMiddle.Count);
        Assert.Equal(1000, atEnd.Count);
        Assert.Empty(outside);
        Assert.Empty(outside2);
    }

    [Fact]
    public void FindNodesAt_NestedIntervals_ReturnsAllLevels()
    {
        // Arrange - deeply nested intervals
        var intervals = new List<(uint, uint, uint)>();
        for (uint i = 0; i < 500; i++)
        {
            intervals.Add((i, 1000 - i, i + 1));
        }
        var tree = BuildTree(intervals);

        // Act
        var results = tree.FindNodesAt(500);

        // Assert - only intervals with start <= 500 <= end match:
        // start i <= 500 and 1000 - i >= 500 => i in [0, 500]
        var expected = new List<uint>();
        for (uint i = 0; i < 500; i++)
        {
            if (i <= 500 && 500 <= 1000 - i)
            {
                expected.Add(i + 1);
            }
        }
        expected.Sort();
        Assert.Equal(expected.Count, results.Count);
        Assert.Equal(expected, results);
    }

    [Fact]
    public void FindNodesAt_AfterAddFollowingQueries_IndexIsRebuilt()
    {
        // Arrange - mutating the tree must invalidate the query index
        var tree = new IntervalTree();
        tree.Add(0, 10, 100);

        var first = tree.FindNodesAt(5);
        Assert.Single(first);

        // Act - add an overlapping interval after the index was built
        tree.Add(5, 15, 200);
        var second = tree.FindNodesAt(5);
        var third = tree.FindNodesAt(12);

        // Assert
        Assert.Equal(2, second.Count);
        Assert.Contains(100u, second);
        Assert.Contains(200u, second);
        Assert.Single(third);
        Assert.Contains(200u, third);
    }

    [Fact]
    public void FindNodesAt_SerializedRoundTrip_LargeTreeMatchesBruteForce()
    {
        // Arrange
        var intervals = GenerateIntervals(2000, seed: 123, maxStart: 50_000, maxLength: 500);
        var tree = BuildTree(intervals);
        var deserialized = IntervalTree.Deserialize(tree.Serialize());

        // Act & Assert
        var rng = new Random(99);
        for (int q = 0; q < 200; q++)
        {
            uint point = (uint)rng.Next(0, 52_000);
            AssertEqualResults(intervals, deserialized, point);
        }
    }

    [Fact]
    public void FindNodesAt_OnLargeTree_IsSubLinearInTreeSize()
    {
        // Arrange - the tree must beat a linear scan by a wide margin on a large input
        const int Count = 200_000;
        const int Queries = 300;
        var intervals = GenerateIntervals(Count, seed: 555, maxStart: 10_000_000, maxLength: 100);
        var tree = BuildTree(intervals);
        var rng = new Random(777);

        var points = new uint[Queries];
        for (int i = 0; i < Queries; i++)
        {
            points[i] = (uint)rng.Next(0, 10_000_200);
        }

        // Warm up (JIT + lazy index build)
        tree.FindNodesAt(5_000_000);

        // Act - measure a reference linear scan
        var stopwatch = Stopwatch.StartNew();
        long bruteForceHits = 0;
        foreach (var point in points)
        {
            foreach (var (start, end, _) in intervals)
            {
                if (start <= point && point <= end)
                {
                    bruteForceHits++;
                }
            }
        }
        stopwatch.Stop();
        var linearTime = stopwatch.ElapsedMilliseconds;

        stopwatch.Restart();
        long treeHits = 0;
        foreach (var point in points)
        {
            treeHits += tree.FindNodesAt(point).Count;
        }
        stopwatch.Stop();
        var treeTime = stopwatch.ElapsedMilliseconds;

        // Assert - identical hit counts, and the tree is at least 10x faster
        Assert.Equal(bruteForceHits, treeHits);
        Assert.True(
            treeTime * 10 < Math.Max(linearTime, 1),
            $"Tree query took {treeTime}ms vs {linearTime}ms for the linear scan");
    }

    private static void AssertEqualResults(List<(uint Start, uint End, uint Offset)> intervals, IntervalTree tree, uint point)
    {
        var expected = BruteForce(intervals, point);
        var actual = tree.FindNodesAt(point);
        Assert.Equal(expected.Count, actual.Count);

        // Same set of node offsets (order-insensitive)
        var actualSorted = new List<uint>(actual);
        actualSorted.Sort();
        Assert.Equal(expected, actualSorted);

        // Results are documented to be ordered by ascending interval start
        var startByOffset = new Dictionary<uint, uint>();
        foreach (var (start, _, offset) in intervals)
        {
            startByOffset[offset] = start;
        }
        for (int i = 1; i < actual.Count; i++)
        {
            Assert.True(
                startByOffset[actual[i - 1]] <= startByOffset[actual[i]],
                $"Results not ordered by start at position {i} for point {point}");
        }
    }
}
