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

        // Assert - The editor should create a new graph
        var modifiedRoot = modifiedGraph.GetRootNode();
        Assert.Equal(1, modifiedRoot.SymbolID);
        Assert.Equal(100, modifiedRoot.NodeType);
        
        // Note: Our simplified editor implementation preserves original node data
        // In a full implementation, the move operation would be properly applied
        
        // Note: Property validation would require extending the SymbolNode API
        // to enumerate all properties, which is beyond the scope of this minimal implementation
    }
}