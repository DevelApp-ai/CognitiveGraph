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
}