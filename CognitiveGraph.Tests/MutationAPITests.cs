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
using Xunit;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for the graph mutation API
/// </summary>
public class MutationAPITests
{
    [Fact]
    public void CognitiveGraphEditor_WithNoOperations_ReturnsOriginalGraph()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "TestNode")
        };

        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 4,
            properties: properties
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var originalGraph = new CognitiveGraph(buffer);
        
        // Act
        using var editor = new CognitiveGraphEditor(originalGraph);
        using var resultGraph = editor.Build();

        // Assert
        var originalRoot = originalGraph.GetRootNode();
        var resultRoot = resultGraph.GetRootNode();
        
        Assert.Equal(originalRoot.SymbolID, resultRoot.SymbolID);
        Assert.Equal(originalRoot.NodeType, resultRoot.NodeType);
        Assert.Equal("test", resultGraph.GetSourceText());
    }

    [Fact]
    public void CognitiveGraphEditor_QueueOperations_IncrementsOperationCount()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        using var editor = new CognitiveGraphEditor(graph);
        
        Assert.Equal(0, editor.OperationCount);
        
        editor.InsertNode(0, 2, 101, 0, 2);
        Assert.Equal(1, editor.OperationCount);
        
        editor.UpdateProperty(rootNodeOffset, "NewProp", PropertyValueType.String, "value");
        Assert.Equal(2, editor.OperationCount);
        
        editor.DeleteNode(999);
        Assert.Equal(3, editor.OperationCount);
        
        editor.ClearOperations();
        Assert.Equal(0, editor.OperationCount);
    }

    [Fact]
    public void CognitiveGraphEditor_FluentAPI_ChainsOperations()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootNodeOffset = builder.WriteSymbolNode(1, 100, 0, 4);
        var buffer = builder.Build(rootNodeOffset, "test");
        using var graph = new CognitiveGraph(buffer);

        // Act
        using var editor = new CognitiveGraphEditor(graph);
        
        var result = editor
            .InsertNode(0, 2, 101, 0, 2)
            .UpdateProperty(rootNodeOffset, "NewProp", PropertyValueType.String, "value")
            .MoveNode(rootNodeOffset, 1, 3)
            .RemoveProperty(rootNodeOffset, "OldProp");

        // Assert
        Assert.Same(editor, result); // Fluent API returns self
        Assert.Equal(4, editor.OperationCount);
    }

    [Fact]
    public void InsertNodeOperation_CreatesCorrectOperation()
    {
        // Arrange & Act
        var operation = new InsertNodeOperation(
            targetOffset: 100,
            symbolId: 42,
            nodeType: 200,
            sourceStart: 5,
            sourceLength: 10,
            packedNodeOffsets: null,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "TestNode")
            }
        );

        // Assert
        Assert.Equal(100u, operation.TargetOffset);
        Assert.Equal(42, operation.SymbolId);
        Assert.Equal(200, operation.NodeType);
        Assert.Equal(5u, operation.SourceStart);
        Assert.Equal(10u, operation.SourceLength);
        Assert.Single(operation.Properties!);
        Assert.Equal("Name", operation.Properties![0].key);
    }

    [Fact]
    public void ReplaceNodeOperation_CreatesCorrectOperation()
    {
        // Arrange & Act
        var operation = new ReplaceNodeOperation(
            targetOffset: 200,
            newSymbolId: 99,
            newNodeType: 300,
            newSourceStart: 15,
            newSourceLength: 20
        );

        // Assert
        Assert.Equal(200u, operation.TargetOffset);
        Assert.Equal(99, operation.NewSymbolId);
        Assert.Equal(300, operation.NewNodeType);
        Assert.Equal(15u, operation.NewSourceStart);
        Assert.Equal(20u, operation.NewSourceLength);
    }

    [Fact]
    public void DeleteNodeOperation_CreatesCorrectOperation()
    {
        // Arrange & Act
        var operation = new DeleteNodeOperation(300);

        // Assert
        Assert.Equal(300u, operation.TargetOffset);
    }

    [Fact]
    public void MoveNodeOperation_CreatesCorrectOperation()
    {
        // Arrange & Act
        var operation = new MoveNodeOperation(400, 25, 30);

        // Assert
        Assert.Equal(400u, operation.TargetOffset);
        Assert.Equal(25u, operation.NewSourceStart);
        Assert.Equal(30u, operation.NewSourceLength);
    }

    [Fact]
    public void UpdatePropertyOperation_CreatesCorrectOperations()
    {
        // Arrange & Act
        var updateOperation = new UpdatePropertyOperation(
            500,
            "TestProperty",
            PropertyValueType.Int32,
            42
        );

        var removeOperation = new UpdatePropertyOperation(500, "RemoveMe");

        // Assert
        Assert.Equal(500u, updateOperation.TargetOffset);
        Assert.Equal("TestProperty", updateOperation.PropertyKey);
        Assert.Equal(PropertyValueType.Int32, updateOperation.PropertyType);
        Assert.Equal(42, updateOperation.PropertyValue);
        Assert.False(updateOperation.RemoveProperty);

        Assert.Equal(500u, removeOperation.TargetOffset);
        Assert.Equal("RemoveMe", removeOperation.PropertyKey);
        Assert.True(removeOperation.RemoveProperty);
    }

    [Fact] 
    public void CognitiveGraphEditor_WithBatchOperations_ProcessesEfficiently()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "OriginalNode"),
            ("Value", PropertyValueType.Int32, 123)
        };

        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 4,
            properties: properties
        );

        var buffer = builder.Build(rootNodeOffset, "test");
        using var originalGraph = new CognitiveGraph(buffer);

        // Act - Apply multiple operations in a batch
        using var editor = new CognitiveGraphEditor(originalGraph);
        
        editor
            .UpdateProperty(rootNodeOffset, "NodeType", PropertyValueType.String, "ModifiedNode")
            .UpdateProperty(rootNodeOffset, "NewProperty", PropertyValueType.Boolean, true)
            .MoveNode(rootNodeOffset, 1, 3);

        using var modifiedGraph = editor.Build();

        // Assert - The editor creates a new graph with all operations applied
        var modifiedRoot = modifiedGraph.GetRootNode();
        Assert.Equal(1, modifiedRoot.SymbolID);
        Assert.Equal(100, modifiedRoot.NodeType);

        // The move operation is applied
        Assert.Equal(1u, modifiedRoot.SourceStart);
        Assert.Equal(3u, modifiedRoot.SourceLength);

        // Property updates are applied and existing properties are preserved
        Assert.True(modifiedRoot.TryGetProperty("NodeType", out var nodeType));
        Assert.Equal("ModifiedNode", nodeType.AsString());
        Assert.True(modifiedRoot.TryGetProperty("Value", out var value));
        Assert.Equal(123, value.AsInt32());
        Assert.True(modifiedRoot.TryGetProperty("NewProperty", out var newProperty));
        Assert.True(newProperty.AsBoolean());
    }

    /// <summary>
    /// Builds a graph exercising ambiguity (two packed nodes / derivations),
    /// SPPF sharing (both derivations reference the same child), CPG edges with
    /// properties, and multi-typed node properties.
    /// </summary>
    private static CognitiveGraph BuildTestGraph()
    {
        using var builder = new CognitiveGraphBuilder();

        var childOffset = builder.WriteSymbolNode(
            symbolId: 7,
            nodeType: 107,
            sourceStart: 5,
            sourceLength: 3,
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "child"),
                ("Count", PropertyValueType.Int32, 9)
            });

        var edgePropertiesOffset = (uint)builder.WritePropertyList(
            new List<(string key, PropertyValueType type, object value)>
            {
                ("label", PropertyValueType.String, "ast")
            });

        var firstPackedOffset = builder.WritePackedNode(
            ruleId: 10,
            childNodeOffsets: new List<uint> { childOffset },
            cpgEdges: new List<CpgEdgeData>
            {
                new((ushort)EdgeType.AST_CHILD, childOffset, edgePropertiesOffset)
            });

        // Second derivation sharing the same child node (SPPF sharing)
        var secondPackedOffset = builder.WritePackedNode(11, new List<uint> { childOffset });

        var rootOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 10,
            packedNodeOffsets: new List<uint> { firstPackedOffset, secondPackedOffset },
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "root"),
                ("Value", PropertyValueType.Int32, 42)
            });

        var buffer = builder.Build(rootOffset, "test source");
        return new CognitiveGraph(buffer);
    }

    [Fact]
    public void Build_WithNoOperations_ReturnsDistinctEqualClone()
    {
        // Arrange
        using var original = BuildTestGraph();
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var clone = editor.Build();

        // Assert - a distinct instance with equal content
        Assert.NotSame(original, clone);
        Assert.Equal("test source", clone.GetSourceText());

        var root = clone.GetRootNode();
        Assert.Equal(1, root.SymbolID);
        Assert.Equal(100, root.NodeType);
        Assert.Equal(0u, root.SourceStart);
        Assert.Equal(10u, root.SourceLength);

        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("root", name.AsString());
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal(42, value.AsInt32());

        // Ambiguity (two derivations) is preserved
        var packedNodes = root.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        Assert.Equal(10, packedNodes[0].RuleID);
        Assert.Equal(11, packedNodes[1].RuleID);

        // SPPF sharing is preserved: both derivations reference the same child
        var firstChildren = packedNodes[0].GetChildNodes();
        var secondChildren = packedNodes[1].GetChildNodes();
        Assert.Equal(1, firstChildren.Count);
        Assert.Equal(1, secondChildren.Count);
        Assert.Equal(firstChildren[0].Offset, secondChildren[0].Offset);
        Assert.Equal(7, firstChildren[0].SymbolID);
        Assert.True(firstChildren[0].TryGetProperty("Name", out var childName));
        Assert.Equal("child", childName.AsString());
        Assert.True(firstChildren[0].TryGetProperty("Count", out var count));
        Assert.Equal(9, count.AsInt32());

        // CPG edges are re-targeted to the cloned nodes and keep their properties
        var edges = packedNodes[0].GetCpgEdges();
        Assert.Equal(1, edges.Count);
        Assert.Equal(EdgeType.AST_CHILD, edges[0].EdgeType);
        Assert.Equal(7, edges[0].GetTargetNode().SymbolID);
        Assert.True(edges[0].TryGetProperty("label", out var label));
        Assert.Equal("ast", label.AsString());
    }

    [Fact]
    public void UpdateProperty_PreservesExistingProperties()
    {
        // Arrange
        using var original = BuildTestGraph();
        var rootOffset = original.GetRootNode().Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var modified = editor
            .UpdateProperty(rootOffset, "Name", PropertyValueType.String, "renamed")
            .Build();

        // Assert - the updated property is applied...
        var root = modified.GetRootNode();
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("renamed", name.AsString());

        // ...and the other original property is preserved
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal(42, value.AsInt32());

        // Packed nodes and children survive the property update
        var packedNodes = root.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        Assert.True(packedNodes[0].GetChildNodes()[0].TryGetProperty("Count", out var count));
        Assert.Equal(9, count.AsInt32());
    }

    [Fact]
    public void RemoveProperty_RemovesOnlyTheTargetedProperty()
    {
        // Arrange
        using var original = BuildTestGraph();
        var rootOffset = original.GetRootNode().Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var modified = editor.RemoveProperty(rootOffset, "Value").Build();

        // Assert
        var root = modified.GetRootNode();
        Assert.False(root.TryGetProperty("Value", out _));
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("root", name.AsString());
    }

    [Fact]
    public void MoveNode_PreservesPackedNodesAndChildren()
    {
        // Arrange
        using var original = BuildTestGraph();
        var rootOffset = original.GetRootNode().Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var modified = editor.MoveNode(rootOffset, 2, 5).Build();

        // Assert - the source position changed...
        var root = modified.GetRootNode();
        Assert.Equal(2u, root.SourceStart);
        Assert.Equal(5u, root.SourceLength);

        // ...while ambiguity, packed nodes, children, sharing, edges and
        // properties are all preserved
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("root", name.AsString());

        var packedNodes = root.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        Assert.Equal(10, packedNodes[0].RuleID);
        Assert.Equal(11, packedNodes[1].RuleID);

        var firstChildren = packedNodes[0].GetChildNodes();
        var secondChildren = packedNodes[1].GetChildNodes();
        Assert.Equal(1, firstChildren.Count);
        Assert.Equal(1, secondChildren.Count);
        Assert.Equal(firstChildren[0].Offset, secondChildren[0].Offset);
        Assert.Equal(7, firstChildren[0].SymbolID);
        Assert.True(firstChildren[0].TryGetProperty("Count", out var count));
        Assert.Equal(9, count.AsInt32());

        var edges = packedNodes[0].GetCpgEdges();
        Assert.Equal(1, edges.Count);
        Assert.Equal(7, edges[0].GetTargetNode().SymbolID);
        Assert.True(edges[0].TryGetProperty("label", out var label));
        Assert.Equal("ast", label.AsString());
    }

    [Fact]
    public void UpdateProperty_OnChildNode_PreservesParentStructure()
    {
        // Arrange
        using var original = BuildTestGraph();
        var childOffset = original.GetRootNode().GetPackedNodes()[0].GetChildNodes()[0].Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var modified = editor
            .UpdateProperty(childOffset, "Name", PropertyValueType.String, "renamed-child")
            .Build();

        // Assert - the child property is updated
        var root = modified.GetRootNode();
        var child = root.GetPackedNodes()[0].GetChildNodes()[0];
        Assert.True(child.TryGetProperty("Name", out var childName));
        Assert.Equal("renamed-child", childName.AsString());
        Assert.True(child.TryGetProperty("Count", out var count));
        Assert.Equal(9, count.AsInt32());

        // ...and the parent structure is untouched
        Assert.True(root.TryGetProperty("Name", out var rootName));
        Assert.Equal("root", rootName.AsString());
        Assert.Equal(2, root.GetPackedNodes().Count);
    }

    [Fact]
    public void DeleteNode_RemovesSubtreeFromResult()
    {
        // Arrange
        using var original = BuildTestGraph();
        var childOffset = original.GetRootNode().GetPackedNodes()[0].GetChildNodes()[0].Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act
        using var modified = editor.DeleteNode(childOffset).Build();

        // Assert - the deleted child is dropped from every derivation
        var root = modified.GetRootNode();
        var packedNodes = root.GetPackedNodes();
        Assert.Equal(2, packedNodes.Count);
        Assert.Equal(0, packedNodes[0].GetChildNodes().Count);
        Assert.Equal(0, packedNodes[1].GetChildNodes().Count);

        // The edge targeting the deleted node is dropped as well
        Assert.Equal(0, packedNodes[0].GetCpgEdges().Count);

        // The root itself is untouched
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("root", name.AsString());
    }

    [Fact]
    public void DeleteNode_OnRoot_ThrowsInvalidOperationException()
    {
        // Arrange
        using var original = BuildTestGraph();
        var rootOffset = original.GetRootNode().Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act & Assert - deleting the root would leave an empty graph
        editor.DeleteNode(rootOffset);
        Assert.Throws<InvalidOperationException>(() => editor.Build());
    }

    [Fact]
    public void Build_CanBeCalledRepeatedly()
    {
        // Arrange
        using var original = BuildTestGraph();
        var rootOffset = original.GetRootNode().Offset;
        using var editor = new CognitiveGraphEditor(original);

        // Act - build twice with operations added in between
        using var first = editor.UpdateProperty(rootOffset, "Name", PropertyValueType.String, "first").Build();
        using var second = editor.UpdateProperty(rootOffset, "Name", PropertyValueType.String, "second").Build();

        // Assert
        Assert.True(first.GetRootNode().TryGetProperty("Name", out var firstName));
        Assert.Equal("first", firstName.AsString());
        Assert.True(second.GetRootNode().TryGetProperty("Name", out var secondName));
        Assert.Equal("second", secondName.AsString());
    }
}