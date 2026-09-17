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
using Xunit;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph;
using CognitiveGraph.Accessors;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for spatial indexing and interval tree functionality
/// </summary>
public class SpatialIndexTests
{
    [Fact]
    public void IntervalTree_AddAndSerialize_WorksCorrectly()
    {
        // Arrange
        var tree = new IntervalTree();
        
        // Act
        tree.Add(0, 5, 100);   // "hello"
        tree.Add(6, 6, 200);   // " "
        tree.Add(7, 11, 300);  // "world"
        
        var serialized = tree.Serialize();
        var deserialized = IntervalTree.Deserialize(serialized);
        
        // Assert
        var results = deserialized.FindNodesAt(2); // Inside "hello"
        Assert.Contains(100u, results);
        Assert.DoesNotContain(200u, results);
        Assert.DoesNotContain(300u, results);
        
        var spaceResults = deserialized.FindNodesAt(6); // The space
        Assert.Contains(200u, spaceResults);
        Assert.DoesNotContain(100u, spaceResults);
        Assert.DoesNotContain(300u, spaceResults);
    }

    [Fact]
    public void FindNodesAt_WithMultipleNodes_ReturnsCorrectOffsets()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        // Create multiple nodes with different source positions
        var helloNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 5,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "hello")
            }
        );
        
        var worldNodeOffset = builder.WriteSymbolNode(
            symbolId: 2,
            nodeType: 101,
            sourceStart: 6,
            sourceLength: 5,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "world")
            }
        );

        var buffer = builder.Build(helloNodeOffset, "hello world");
        using var graph = new CognitiveGraph(buffer);

        // Act & Assert
        var helloResults = graph.FindNodesAt(2); // Inside "hello"
        Assert.Contains(helloNodeOffset, helloResults);
        Assert.DoesNotContain(worldNodeOffset, helloResults);

        var worldResults = graph.FindNodesAt(8); // Inside "world"
        Assert.Contains(worldNodeOffset, worldResults);
        Assert.DoesNotContain(helloNodeOffset, worldResults);

        var noResults = graph.FindNodesAt(5); // The space between
        Assert.Empty(noResults);
    }

    [Fact]
    public void ProcessNodesAt_WithCustomProcessor_ProcessesCorrectNodes()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var nodeOffset = builder.WriteSymbolNode(
            symbolId: 42,
            nodeType: 200,
            sourceStart: 0,
            sourceLength: 10,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("NodeType", PropertyValueType.String, "TestNode"),
                ("Value", PropertyValueType.Int32, 123)
            }
        );

        var buffer = builder.Build(nodeOffset, "test input");
        using var graph = new CognitiveGraph(buffer);

        var processedNodes = new List<(ushort symbolId, string nodeType, int value)>();

        // Act
        graph.ProcessNodesAt(5, (in SymbolNode node) =>
        {
            if (node.TryGetProperty("NodeType", out var nodeTypeProperty) &&
                node.TryGetProperty("Value", out var valueProperty))
            {
                processedNodes.Add((node.SymbolID, nodeTypeProperty.AsString(), valueProperty.AsInt32()));
            }
        });

        // Assert
        Assert.Single(processedNodes);
        var (symbolId, nodeType, value) = processedNodes[0];
        Assert.Equal(42, symbolId);
        Assert.Equal("TestNode", nodeType);
        Assert.Equal(123, value);
    }

    [Fact]
    public void FindNodesAt_WithOverlappingIntervals_ReturnsAllMatching()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        // Create overlapping nodes (e.g., expression and sub-expressions)
        var outerNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 15,  // Covers entire "hello + world"
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Type", PropertyValueType.String, "BinaryExpression")
            }
        );
        
        var leftNodeOffset = builder.WriteSymbolNode(
            symbolId: 2,
            nodeType: 101,
            sourceStart: 0,
            sourceLength: 5,   // Just "hello"
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Type", PropertyValueType.String, "Identifier")
            }
        );

        var buffer = builder.Build(outerNodeOffset, "hello + world");
        using var graph = new CognitiveGraph(buffer);

        // Act - Query position that overlaps both nodes
        var results = graph.FindNodesAt(2);

        // Assert - Both nodes should be returned
        Assert.Equal(2, results.Count);
        Assert.Contains(outerNodeOffset, results);
        Assert.Contains(leftNodeOffset, results);
    }

    [Fact]
    public void FindNodesAt_WithCaching_ImprovesPerformance()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var nodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 10
        );

        var buffer = builder.Build(nodeOffset, "test input");
        using var graph = new CognitiveGraph(buffer);

        // Act - Call multiple times with same position
        var results1 = graph.FindNodesAt(5);
        var results2 = graph.FindNodesAt(5);
        var results3 = graph.FindNodesAt(5);

        // Assert - Results should be consistent
        Assert.Equal(results1.Count, results2.Count);
        Assert.Equal(results1.Count, results3.Count);
        
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i], results2[i]);
            Assert.Equal(results1[i], results3[i]);
        }
    }

    [Fact]
    public void IntervalTree_WithEmptyTree_ReturnsEmptyResults()
    {
        // Arrange
        var tree = new IntervalTree();
        
        // Act
        var results = tree.FindNodesAt(10);
        
        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void IntervalTree_GetSerializedSize_ReturnsCorrectSize()
    {
        // Arrange
        var tree = new IntervalTree();
        tree.Add(0, 10, 100);
        tree.Add(5, 15, 200);
        tree.Add(20, 30, 300);
        
        // Act
        var expectedSize = tree.GetSerializedSize();
        var actualSize = (uint)tree.Serialize().Length;
        
        // Assert
        Assert.Equal(expectedSize, actualSize);
        
        // Size should be: 4 bytes (count) + 3 * 12 bytes (IntervalNode.SIZE)
        Assert.Equal(4u + 3u * 12u, expectedSize);
    }

    [Fact]
    public void FindNodesAt_WithFilePersistence_WorksCorrectly()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Create and save to file
            using (var builder = new CognitiveGraphBuilder())
            {
                var nodeOffset = builder.WriteSymbolNode(
                    symbolId: 99,
                    nodeType: 150,
                    sourceStart: 3,
                    sourceLength: 7,
                    properties: new List<(string, PropertyValueType, object)>
                    {
                        ("Name", PropertyValueType.String, "testNode")
                    }
                );

                using var fileStream = File.Create(tempFile);
                builder.Build(fileStream, nodeOffset, "foo bar baz");
            }

            // Load from file and test spatial query
            using var graph = new CognitiveGraph(tempFile);
            
            // Act
            var results = graph.FindNodesAt(5); // Inside "bar"
            
            // Assert
            Assert.Single(results);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void IntervalTree_FindNodesAt_NestedAndOverlappingIntervals_ReturnsAllMatches()
    {
        // Arrange - nested + overlapping intervals sharing points
        var tree = new IntervalTree();
        tree.Add(10, 90, 1000);  // outer
        tree.Add(10, 90, 1001);  // duplicate span, different node
        tree.Add(20, 40, 1002);  // nested in outer
        tree.Add(35, 60, 1003);  // overlaps nested
        tree.Add(90, 90, 1004);  // point interval at the outer end
        tree.Add(0, 5, 1005);    // disjoint, before everything
        tree.Add(200, 300, 1006);// disjoint, after everything

        // Act & Assert
        Assert.Equal(new List<uint> { 1000u, 1001u }, tree.FindNodesAt(10)); // outer start
        Assert.Equal(new List<uint> { 1000u, 1001u, 1002u }, tree.FindNodesAt(25)); // inside nested
        Assert.Equal(new List<uint> { 1000u, 1001u, 1002u, 1003u }, tree.FindNodesAt(38)); // nested + overlap
        Assert.Equal(new List<uint> { 1000u, 1001u, 1003u }, tree.FindNodesAt(50)); // outer + overlap
        Assert.Equal(new List<uint> { 1000u, 1001u, 1004u }, tree.FindNodesAt(90)); // boundary end
        Assert.Equal(new List<uint> { 1005u }, tree.FindNodesAt(3));
        Assert.Equal(new List<uint> { 1006u }, tree.FindNodesAt(250));
        Assert.Empty(tree.FindNodesAt(95)); // between disjoint ranges
        Assert.Empty(tree.FindNodesAt(6));  // gap
    }

    [Fact]
    public void IntervalTree_FindNodesAt_MatchesLinearScanOnRandomizedData()
    {
        // Arrange - deterministic random intervals, compared against a brute-force scan
        const int intervalCount = 2000;
        const int queryCount = 500;
        var random = new Random(20260916);

        var intervals = new List<(uint start, uint end, uint offset)>(intervalCount);
        for (int i = 0; i < intervalCount; i++)
        {
            var start = (uint)random.Next(0, 10_000);
            var length = (uint)random.Next(0, 200);
            intervals.Add((start, start + length, (uint)(i + 1)));
        }

        var tree = new IntervalTree();
        foreach (var (start, end, offset) in intervals)
            tree.Add(start, end, offset);

        // Act & Assert
        for (int q = 0; q < queryCount; q++)
        {
            var point = (uint)random.Next(0, 10_500);

            var expected = intervals
                .Where(iv => iv.start <= point && point <= iv.end)
                .Select(iv => iv.offset)
                .OrderBy(o => o)
                .ToList();

            var actual = tree.FindNodesAt(point).OrderBy(o => o).ToList();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void IntervalTree_FindNodesAt_LargeTree_FindsNeedleIntervals()
    {
        // Arrange - 100_000 background intervals covering nothing in the middle,
        // plus a few "needles" a linear scan would have to walk the whole list for.
        var tree = new IntervalTree();
        const uint needleOffset = 4_242_424u;
        const int count = 100_000;
        for (int i = 0; i < count; i++)
        {
            var start = (uint)i * 10;
            tree.Add(start, start + 3, (uint)(i + 1)); // [i*10, i*10+3]
        }
        tree.Add(500_000, 600_000, needleOffset);

        // Act & Assert - 550005 falls in a gap between background intervals (i*10+3 < 550005 < 550010)
        Assert.Equal(new List<uint> { needleOffset }, tree.FindNodesAt(550005));
        var hits = tree.FindNodesAt(250_000); // inside a single background interval
        Assert.Single(hits);
        var boundary = tree.FindNodesAt(500_000);
        Assert.Contains(needleOffset, boundary);
        Assert.Equal(2, boundary.Count); // background [500_000,500_003] + needle
    }

    [Fact]
    public void IntervalTree_FindNodesAt_AfterAddInvalidatesIndex_UsesFreshData()
    {
        // Arrange
        var tree = new IntervalTree();
        tree.Add(0, 10, 100);

        // Prime the centered index with a first query
        Assert.Single(tree.FindNodesAt(5));

        // Act - mutate after the index has been built
        tree.Add(20, 30, 200);

        // Assert - the new interval is found (index was invalidated and rebuilt)
        Assert.Equal(new List<uint> { 200u }, tree.FindNodesAt(25));
        Assert.Equal(new List<uint> { 100u }, tree.FindNodesAt(5));
    }

    [Fact]
    public void IntervalTree_FindNodesAt_SerializedRoundTrip_MatchesOriginal()
    {
        // Arrange
        var tree = new IntervalTree();
        var random = new Random(42);
        for (int i = 0; i < 500; i++)
        {
            var start = (uint)random.Next(0, 5000);
            tree.Add(start, start + (uint)random.Next(0, 100), (uint)(i + 1));
        }

        var deserialized = IntervalTree.Deserialize(tree.Serialize());

        // Act & Assert
        for (uint point = 0; point < 5100; point += 7)
        {
            var expected = tree.FindNodesAt(point).OrderBy(o => o).ToList();
            var actual = deserialized.FindNodesAt(point).OrderBy(o => o).ToList();
            Assert.Equal(expected, actual);
        }
    }
}