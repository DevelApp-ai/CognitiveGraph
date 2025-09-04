using Xunit;
using CognitiveGraph.Core;
using CognitiveGraph.Core.Builder;
using CognitiveGraph.Core.Schema;
using System.Collections.Generic;

namespace CognitiveGraph.Tests;

public class CognitiveGraphAdvancedTests
{
    [Fact]
    public void CreateGraphWithPackedNodes_ShouldWorkCorrectly()
    {
        // Arrange - Create a simple node with packed nodes (without CPG edges for simplicity)
        using var builder = new CognitiveGraphBuilder();

        var callProperties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "CallExpression"),
            ("FunctionName", PropertyValueType.String, "foo")
        };

        // Create a packed node without CPG edges
        var packedNodeOffset = builder.WritePackedNode(ruleId: 1);

        var callNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 300, // CallExpression
            sourceStart: 0,
            sourceLength: 6, // "foo(x)"
            packedNodeOffsets: new List<uint> { packedNodeOffset },
            properties: callProperties
        );

        var buffer = builder.Build(callNodeOffset, "foo(x)");

        // Act
        using var graph = new CognitiveGraph.Core.CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();

        // Assert
        Assert.Equal("foo(x)", graph.GetSourceText());
        Assert.Equal(300, rootNode.NodeType);
        Assert.True(rootNode.TryGetProperty("FunctionName", out var funcName));
        Assert.Equal("foo", funcName.AsString());

        // Check packed nodes
        var packedNodes = rootNode.GetPackedNodes();
        Assert.Equal(1, packedNodes.Count);
        
        var firstPackedNode = packedNodes[0];
        Assert.Equal(1, firstPackedNode.RuleID);
        
        // Since no CPG edges were added, should be empty
        var edges = firstPackedNode.GetCpgEdges();
        Assert.Equal(0, edges.Count);
    }

    [Fact]
    public void PropertyValueTypes_ShouldWorkCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();

        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("StringProp", PropertyValueType.String, "test string"),
            ("IntProp", PropertyValueType.Int32, 42),
            ("BoolProp", PropertyValueType.Boolean, true),
            ("DoubleProp", PropertyValueType.Double, 3.14159)
        };

        var nodeOffset = builder.WriteSymbolNode(1, 100, 0, 4, properties: properties);
        var buffer = builder.Build(nodeOffset, "test");

        // Act
        using var graph = new CognitiveGraph.Core.CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();

        // Assert
        Assert.True(rootNode.TryGetProperty("StringProp", out var stringProp));
        Assert.Equal("test string", stringProp.AsString());

        Assert.True(rootNode.TryGetProperty("IntProp", out var intProp));
        Assert.Equal(42, intProp.AsInt32());

        Assert.True(rootNode.TryGetProperty("BoolProp", out var boolProp));
        Assert.True(boolProp.AsBoolean());

        Assert.True(rootNode.TryGetProperty("DoubleProp", out var doubleProp));
        Assert.Equal(3.14159, doubleProp.AsDouble(), precision: 5);
    }

    [Fact]
    public void NodeAmbiguity_ShouldBeDetectedCorrectly()
    {
        // Arrange - Create a node with multiple packed nodes to simulate ambiguity
        using var builder = new CognitiveGraphBuilder();

        var packedNode1 = builder.WritePackedNode(ruleId: 1);
        var packedNode2 = builder.WritePackedNode(ruleId: 2);

        var ambiguousNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 5,
            packedNodeOffsets: new List<uint> { packedNode1, packedNode2 }
        );

        var buffer = builder.Build(ambiguousNodeOffset, "a+b*c");

        // Act
        using var graph = new CognitiveGraph.Core.CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();

        // Assert
        Assert.True(rootNode.IsAmbiguous);
        
        var packedNodes = rootNode.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        
        Assert.Equal(1, packedNodes[0].RuleID);
        Assert.Equal(2, packedNodes[1].RuleID);
    }

    [Fact]
    public void EmptyCollections_ShouldBeHandledCorrectly()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();

        var nodeOffset = builder.WriteSymbolNode(1, 100, 0, 4); // No packed nodes or properties
        var buffer = builder.Build(nodeOffset, "test");

        // Act
        using var graph = new CognitiveGraph.Core.CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();

        // Assert
        Assert.False(rootNode.IsAmbiguous);
        
        var packedNodes = rootNode.GetPackedNodes();
        Assert.Equal(0, packedNodes.Count);
        
        var properties = rootNode.GetProperties();
        Assert.Equal(0, properties.Count);
    }
}