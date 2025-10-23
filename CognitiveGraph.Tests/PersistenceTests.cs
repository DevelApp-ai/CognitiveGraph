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

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for large-scale persistence and disk mapping functionality
/// </summary>
public class PersistenceTests
{
    [Fact]
    public void Build_WithFileStream_WriteGraphToFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        
        try
        {
            using var builder = new CognitiveGraphBuilder();
            
            var properties = new List<(string key, PropertyValueType type, object value)>
            {
                ("NodeType", PropertyValueType.String, "BinaryExpression"),
                ("Operator", PropertyValueType.String, "+")
            };

            var rootNodeOffset = builder.WriteSymbolNode(
                symbolId: 1,
                nodeType: 200,
                sourceStart: 0,
                sourceLength: 13,
                properties: properties
            );

            // Act
            using (var fileStream = File.Create(tempFile))
            {
                builder.Build(fileStream, rootNodeOffset, "hello + world");
            }

            // Assert
            Assert.True(File.Exists(tempFile));
            var fileInfo = new FileInfo(tempFile);
            Assert.True(fileInfo.Length > 0);

            // Verify the file can be loaded back
            using var graph = new CognitiveGraph(tempFile);
            var rootNode = graph.GetRootNode();
            
            Assert.True(rootNode.TryGetProperty("Operator", out var op));
            Assert.Equal("+", op.AsString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constructor_WithFilePath_LoadsMemoryMappedFile()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Create a graph file first
            using (var builder = new CognitiveGraphBuilder())
            {
                var properties = new List<(string key, PropertyValueType type, object value)>
                {
                    ("NodeType", PropertyValueType.String, "Identifier"),
                    ("Name", PropertyValueType.String, "hello")
                };

                var rootNodeOffset = builder.WriteSymbolNode(
                    symbolId: 42,
                    nodeType: 100,
                    sourceStart: 0,
                    sourceLength: 5,
                    properties: properties
                );

                using var fileStream = File.Create(tempFile);
                builder.Build(fileStream, rootNodeOffset, "hello");
            }

            // Act
            using var graph = new CognitiveGraph(tempFile);

            // Assert
            var rootNode = graph.GetRootNode();
            Assert.True(rootNode.TryGetProperty("Name", out var nameProperty));
            Assert.Equal("hello", nameProperty.AsString());
            Assert.Equal("hello", graph.GetSourceText());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(Path.GetTempPath(), "non-existent-graph.bin");

        // Act & Assert
        Assert.Throws<FileNotFoundException>(() => new CognitiveGraph(nonExistentFile));
    }

    [Fact]
    public void Constructor_WithInvalidFile_ThrowsArgumentException()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        
        try
        {
            // Write invalid data to file
            File.WriteAllText(tempFile, "This is not a valid cognitive graph");

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new CognitiveGraph(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Build_WithFileStream_DataIntegrityMaintained()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        
        try
        {
            using var builder = new CognitiveGraphBuilder();
            
            var properties = new List<(string key, PropertyValueType type, object value)>
            {
                ("NodeType", PropertyValueType.String, "ComplexExpression"),
                ("LineNumber", PropertyValueType.Int32, 42),
                ("IsPublic", PropertyValueType.Boolean, true),
                ("Complexity", PropertyValueType.Double, 3.14159)
            };

            var rootNodeOffset = builder.WriteSymbolNode(
                symbolId: 123,
                nodeType: 456,
                sourceStart: 5,
                sourceLength: 20,
                properties: properties
            );

            var sourceText = "function complexCalculation() { return x + y * z; }";

            // Act
            using (var fileStream = File.Create(tempFile))
            {
                builder.Build(fileStream, rootNodeOffset, sourceText);
            }

            // Load and verify
            using var graph = new CognitiveGraph(tempFile);
            var rootNode = graph.GetRootNode();

            // Assert all properties are preserved
            Assert.True(rootNode.TryGetProperty("NodeType", out var nodeType));
            Assert.Equal("ComplexExpression", nodeType.AsString());

            Assert.True(rootNode.TryGetProperty("LineNumber", out var lineNumber));
            Assert.Equal(42, lineNumber.AsInt32());

            Assert.True(rootNode.TryGetProperty("IsPublic", out var isPublic));
            Assert.True(isPublic.AsBoolean());

            Assert.True(rootNode.TryGetProperty("Complexity", out var complexity));
            Assert.Equal(3.14159, complexity.AsDouble(), precision: 5);

            Assert.Equal(sourceText, graph.GetSourceText());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}