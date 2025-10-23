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

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for the query engine functionality
/// </summary>
public class QueryEngineTests
{
    [Fact]
    public async Task QueryAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithNullQuery_ReturnsEmptyList()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public void Query_SynchronousVersion_WorksCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "TestNode")
        };

        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 42,
            nodeType: 200,
            sourceStart: 0,
            sourceLength: 4,
            properties: properties
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = graph.Query("symbolId: 42");

        // Assert
        Assert.Single(results);
        Assert.Contains(rootNodeOffset, results);
    }

    [Fact]
    public async Task QueryAsync_WithSymbolIdFilter_ReturnsMatchingNodes()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 123,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 4
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("symbolId: 123");

        // Assert
        Assert.Single(results);
        Assert.Contains(rootNodeOffset, results);
    }

    [Fact]
    public async Task QueryAsync_WithNonMatchingSymbolId_ReturnsEmptyList()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 123,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 4
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("symbolId: 999");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithNodeTypeFilter_ReturnsMatchingNodes()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 456,
            sourceStart: 0,
            sourceLength: 4
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("nodeType: 456");

        // Assert
        Assert.Single(results);
        Assert.Contains(rootNodeOffset, results);
    }

    [Fact]
    public async Task QueryAsync_WithNonMatchingNodeType_ReturnsEmptyList()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 456,
            sourceStart: 0,
            sourceLength: 4
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("nodeType: 999");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithGenericQuery_ReturnsRootNode()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act - Use a generic query that should return the default result
        var results = await graph.QueryAsync("{ nodes }");

        // Assert - Our simple implementation returns root for any unmatched query
        Assert.Single(results);
        Assert.Contains(rootNodeOffset, results);
    }

    [Fact]
    public async Task QueryAsync_WithComplexQuery_ParsesCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "Expression"),
            ("Operator", PropertyValueType.String, "+")
        };

        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 789,
            nodeType: 300,
            sourceStart: 0,
            sourceLength: 13,
            properties: properties
        );

        var buffer = builder.Build(rootNodeOffset, "hello + world");
        using var graph = new CognitiveGraph(buffer);

        // Act
        var results = await graph.QueryAsync("symbolId: 789");

        // Assert
        Assert.Single(results);
        Assert.Contains(rootNodeOffset, results);
    }

    [Fact]
    public async Task QueryAsync_MultipleQueries_CachePerformance()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 555,
            nodeType: 777,
            sourceStart: 0,
            sourceLength: 10
        );

        var buffer = builder.Build(rootNodeOffset, "test_input");
        using var graph = new CognitiveGraph(buffer);

        // Act - Run same query multiple times
        var results1 = await graph.QueryAsync("symbolId: 555");
        var results2 = await graph.QueryAsync("symbolId: 555");
        var results3 = await graph.QueryAsync("symbolId: 555");

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
    public async Task QueryAsync_WithInvalidQuery_HandlesGracefully()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act & Assert - Should not throw, but return default result
        var results = await graph.QueryAsync("invalid query syntax");
        
        // For our simple implementation, invalid queries return the root node
        Assert.Single(results);
    }
}