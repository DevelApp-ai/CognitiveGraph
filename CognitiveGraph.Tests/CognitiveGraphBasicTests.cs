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


using Xunit;
using CognitiveGraph;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Tests;

public class CognitiveGraphBasicTests
{
    [Fact]
    public void CreateSimpleGraph_ShouldWorkCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        // Create a simple expression node: "hello world"
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "StringLiteral"),
            ("Value", PropertyValueType.String, "hello world")
        };
        
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100, // StringLiteral
            sourceStart: 0,
            sourceLength: 11,
            properties: properties
        );

        var buffer = builder.Build(rootNodeOffset, "hello world");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        
        // Assert
        Assert.True(graph.Header.MagicNumber == GraphHeader.MAGIC_NUMBER);
        Assert.Equal("hello world", graph.GetSourceText());
        Assert.True(graph.IsFullyParsed);
        
        var rootNode = graph.GetRootNode();
        
        // Debug: Check what we're actually reading 
        var expectedSymbolId = (ushort)1;
        var actualSymbolId = rootNode.SymbolID;
        
        Assert.Equal(expectedSymbolId, actualSymbolId);
        Assert.Equal(100, rootNode.NodeType);
        Assert.Equal(0u, rootNode.SourceStart);
        Assert.Equal(11u, rootNode.SourceLength);
        
        var nodeProperties = rootNode.GetProperties();
        Assert.Equal(2, nodeProperties.Count);
        
        // Test property access
        Assert.True(rootNode.TryGetProperty("NodeType", out var nodeTypeValue));
        Assert.Equal("StringLiteral", nodeTypeValue.AsString());
        
        Assert.True(rootNode.TryGetProperty("Value", out var valueProperty));
        Assert.Equal("hello world", valueProperty.AsString());
    }

    [Fact]
    public void GraphStatistics_ShouldBeAccurate()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 5);
        var buffer = builder.Build(rootNodeOffset, "hello");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var stats = graph.GetStatistics();
        
        // Assert
        Assert.Equal(1u, stats.NodeCount);
        Assert.Equal(0u, stats.EdgeCount);
        Assert.Equal(5u, stats.SourceLength);
        Assert.True(stats.BufferSize > 0);
    }

    [Fact]
    public void InvalidGraph_ShouldThrowException()
    {
        // Arrange
        var invalidData = new byte[100]; // Buffer without proper header
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => CognitiveGraph.FromBytes(invalidData));
    }

    [Fact]
    public void EmptyPackedNodeCollection_ShouldBeEmpty()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 3);
        var buffer = builder.Build(rootNodeOffset, "foo");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();
        var packedNodes = rootNode.GetPackedNodes();
        
        // Assert
        Assert.Equal(0, packedNodes.Count);
        Assert.False(rootNode.IsAmbiguous);
    }
}