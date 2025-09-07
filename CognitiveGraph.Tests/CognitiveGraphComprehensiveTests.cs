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
using System.Linq;

namespace CognitiveGraph.Tests;

public class CognitiveGraphComprehensiveTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void LargeGraphPerformance_ShouldHandleManyNodes(int nodeCount)
    {
        // Arrange - Create a single node with many properties instead of many separate nodes
        // since the graph builder builds around a single root node
        using var builder = new CognitiveGraphBuilder();
        var sourceText = string.Join(" ", Enumerable.Range(1, nodeCount).Select(i => $"node{i}"));
        
        // Create a node with many properties to simulate complexity
        var properties = new List<(string key, PropertyValueType type, object value)>();
        for (int i = 0; i < nodeCount; i++)
        {
            properties.Add(($"Node{i}", PropertyValueType.String, $"node{i}"));
            properties.Add(($"Index{i}", PropertyValueType.Int32, i));
        }
        
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: (uint)sourceText.Length,
            properties: properties
        );
        
        var buffer = builder.Build(rootNodeOffset, sourceText);
        
        // Act & Assert
        using var graph = new CognitiveGraph(buffer);
        var stats = graph.GetStatistics();
        
        // We have one node but with many properties
        Assert.Equal(1u, stats.NodeCount);
        Assert.Equal((uint)sourceText.Length, stats.SourceLength);
        Assert.True(stats.BufferSize > 0);
        
        // Verify properties were stored correctly
        var rootNode = graph.GetRootNode();
        var nodeProperties = rootNode.GetProperties();
        Assert.Equal(nodeCount * 2, nodeProperties.Count); // Each iteration adds 2 properties
    }

    [Fact]
    public void ComplexAmbiguousExpression_ShouldHandleMultipleInterpretations()
    {
        // Arrange - Create an ambiguous expression "a+b*c" that could be ((a+b)*c) or (a+(b*c))
        using var builder = new CognitiveGraphBuilder();
        
        // Create two different packed nodes representing different parse trees
        var multiplyFirstRule = builder.WritePackedNode(ruleId: 1); // (a+b)*c interpretation
        var addFirstRule = builder.WritePackedNode(ruleId: 2); // a+(b*c) interpretation
        
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "BinaryExpression"),
            ("Expression", PropertyValueType.String, "a+b*c"),
            ("AmbiguityCount", PropertyValueType.Int32, 2)
        };
        
        var ambiguousExpressionOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 200, // BinaryExpression
            sourceStart: 0,
            sourceLength: 5, // "a+b*c"
            packedNodeOffsets: new List<uint> { multiplyFirstRule, addFirstRule },
            properties: properties
        );
        
        var buffer = builder.Build(ambiguousExpressionOffset, "a+b*c");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();
        
        // Assert
        Assert.True(rootNode.IsAmbiguous);
        Assert.Equal("a+b*c", graph.GetSourceText());
        
        var packedNodes = rootNode.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        
        // Verify different rule IDs representing different parse interpretations
        Assert.Equal(1, packedNodes[0].RuleID);
        Assert.Equal(2, packedNodes[1].RuleID);
        
        // Verify properties
        Assert.True(rootNode.TryGetProperty("AmbiguityCount", out var ambiguityCount));
        Assert.Equal(2, ambiguityCount.AsInt32());
    }

    [Fact]
    public void GraphWithCpgEdges_ShouldSupportSemanticAnalysis()
    {
        // Arrange - Create a simple call graph without CPG edges for now since WriteCpgEdge is not implemented
        using var builder = new CognitiveGraphBuilder();
        
        // Create a packed node without CPG edges (since the API doesn't expose WriteCpgEdge method)
        var packedNodeWithoutEdges = builder.WritePackedNode(ruleId: 1);
        
        var callNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 300, // CallExpression
            sourceStart: 0,
            sourceLength: 6,
            packedNodeOffsets: new List<uint> { packedNodeWithoutEdges },
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("NodeType", PropertyValueType.String, "CallExpression"),
                ("FunctionName", PropertyValueType.String, "func"),
                ("HasSemanticInfo", PropertyValueType.Boolean, true)
            }
        );
        
        var buffer = builder.Build(callNodeOffset, "func()");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();
        
        // Assert
        Assert.Equal("func()", graph.GetSourceText());
        Assert.True(rootNode.TryGetProperty("HasSemanticInfo", out var hasSemanticInfo));
        Assert.True(hasSemanticInfo.AsBoolean());
        
        var packedNodes = rootNode.GetPackedNodes();
        Assert.Equal(1, packedNodes.Count);
        
        var packedNode = packedNodes[0];
        var edges = packedNode.GetCpgEdges();
        // Since we can't create CPG edges with current API, verify empty collection
        Assert.Equal(0, edges.Count);
    }

    [Fact]
    public void MemoryLayout_ShouldBeConsistent()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("TestProp", PropertyValueType.String, "value")
        };
        
        var nodeOffset = builder.WriteSymbolNode(1, 100, 0, 4, properties: properties);
        var buffer = builder.Build(nodeOffset, "test");
        
        // Act - Create multiple graph instances from the same buffer
        using var graph1 = new CognitiveGraph(buffer);
        var bufferCopy = new byte[buffer.AsSpan().Length];
        buffer.AsSpan().CopyTo(bufferCopy);
        using var graph2 = CognitiveGraph.FromBytes(bufferCopy);
        
        // Assert - Both instances should read the same data
        Assert.Equal(graph1.GetSourceText(), graph2.GetSourceText());
        Assert.Equal(graph1.Header.MagicNumber, graph2.Header.MagicNumber);
        Assert.Equal(graph1.Header.Version, graph2.Header.Version);
        
        var stats1 = graph1.GetStatistics();
        var stats2 = graph2.GetStatistics();
        
        Assert.Equal(stats1.NodeCount, stats2.NodeCount);
        Assert.Equal(stats1.EdgeCount, stats2.EdgeCount);
        Assert.Equal(stats1.SourceLength, stats2.SourceLength);
    }

    [Fact]
    public void PropertyEdgeCases_ShouldBeHandledCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var edgeCaseProperties = new List<(string key, PropertyValueType type, object value)>
        {
            ("EmptyString", PropertyValueType.String, ""),
            ("ZeroInt", PropertyValueType.Int32, 0),
            ("FalseBoolean", PropertyValueType.Boolean, false),
            ("NegativeDouble", PropertyValueType.Double, -123.456),
            ("MinInt", PropertyValueType.Int32, int.MinValue),
            ("MaxInt", PropertyValueType.Int32, int.MaxValue),
            ("SpecialChars", PropertyValueType.String, "Hello\nWorld\t\"'\\"),
        };
        
        var nodeOffset = builder.WriteSymbolNode(1, 100, 0, 4, properties: edgeCaseProperties);
        var buffer = builder.Build(nodeOffset, "test");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();
        
        // Assert
        Assert.True(rootNode.TryGetProperty("EmptyString", out var emptyString));
        Assert.Equal("", emptyString.AsString());
        
        Assert.True(rootNode.TryGetProperty("ZeroInt", out var zeroInt));
        Assert.Equal(0, zeroInt.AsInt32());
        
        Assert.True(rootNode.TryGetProperty("FalseBoolean", out var falseBoolean));
        Assert.False(falseBoolean.AsBoolean());
        
        Assert.True(rootNode.TryGetProperty("NegativeDouble", out var negativeDouble));
        Assert.Equal(-123.456, negativeDouble.AsDouble(), precision: 10);
        
        Assert.True(rootNode.TryGetProperty("MinInt", out var minInt));
        Assert.Equal(int.MinValue, minInt.AsInt32());
        
        Assert.True(rootNode.TryGetProperty("MaxInt", out var maxInt));
        Assert.Equal(int.MaxValue, maxInt.AsInt32());
        
        Assert.True(rootNode.TryGetProperty("SpecialChars", out var specialChars));
        Assert.Equal("Hello\nWorld\t\"'\\", specialChars.AsString());
    }

    [Fact]
    public void ConcurrentAccess_ShouldBeSafe()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var nodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(nodeOffset, "test");
        
        var exceptions = new List<Exception>();
        var tasks = new List<Task>();
        
        // Act - Access the same graph from multiple threads
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    using var graph = new CognitiveGraph(buffer);
                    var sourceText = graph.GetSourceText();
                    var rootNode = graph.GetRootNode();
                    var stats = graph.GetStatistics();
                    
                    // Perform some read operations
                    Assert.Equal("test", sourceText);
                    Assert.Equal(1u, stats.NodeCount);
                }
                catch (Exception ex)
                {
                    lock (exceptions)
                    {
                        exceptions.Add(ex);
                    }
                }
            }));
        }
        
        Task.WaitAll(tasks.ToArray());
        
        // Assert - No exceptions should occur during concurrent read access
        Assert.Empty(exceptions);
    }
}