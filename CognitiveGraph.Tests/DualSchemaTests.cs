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
using CognitiveGraph.Schema;
using CognitiveGraph.Builder;
using System.Runtime.InteropServices;
using System.IO;

namespace CognitiveGraph.Tests;

public class DualSchemaTests
{
    [Fact]
    public void SchemaVersion_EnumValues_AreCorrect()
    {
        Assert.Equal((ushort)1, (ushort)SchemaVersion.V1);
        Assert.Equal((ushort)2, (ushort)SchemaVersion.V2);
    }

    [Fact]
    public void GraphHeaderPreamble_Size_Is6Bytes()
    {
        Assert.Equal(6, GraphHeaderPreamble.SIZE);
        Assert.Equal(6, Marshal.SizeOf<GraphHeaderPreamble>());
    }

    [Fact]
    public void GraphHeaderPreamble_MagicNumber_IsCorrect()
    {
        var preamble = new GraphHeaderPreamble(GraphHeaderPreamble.MAGIC_NUMBER, 1);
        Assert.Equal((uint)0x434F474E, preamble.MagicNumber);
        Assert.Equal((ushort)1, preamble.Version);
    }

    [Fact]
    public void GraphHeaderV2_Size_Is64Bytes()
    {
        Assert.Equal(64, GraphHeaderV2.SIZE);
        Assert.Equal(64, Marshal.SizeOf<GraphHeaderV2>());
    }

    [Fact]
    public void GraphHeaderV2_Alignment_Is8Bytes()
    {
        // Check that the struct is properly aligned
        var header = new GraphHeaderV2(
            GraphHeaderV2.MAGIC_NUMBER,
            GraphHeaderV2.SCHEMA_VERSION,
            0,
            100,
            200,
            300,
            400,
            500,
            600
        );

        Assert.Equal(GraphHeaderV2.MAGIC_NUMBER, header.MagicNumber);
        Assert.Equal((ushort)2, header.Version);
        Assert.Equal((ulong)100, header.RootNodeOffset);
        Assert.Equal((ulong)200, header.NodeCount);
        Assert.Equal((ulong)300, header.EdgeCount);
        Assert.Equal((ulong)400, header.SourceTextLength);
        Assert.Equal((ulong)500, header.SourceTextOffset);
        Assert.Equal((ulong)600, header.IntervalTreeOffset);
    }

    [Fact]
    public void SymbolNodeDataV2_Size_Is32Bytes()
    {
        Assert.Equal(32, SymbolNodeDataV2.SIZE);
        Assert.Equal(32, Marshal.SizeOf<SymbolNodeDataV2>());
    }

    [Fact]
    public void SymbolNodeDataV2_Fields_AreCorrect()
    {
        var node = new SymbolNodeDataV2(
            symbolId: 100,
            nodeType: 200,
            sourceStart: 300,
            sourceLength: 400,
            packedNodesOffset: 500,
            propertiesOffset: 600
        );

        Assert.Equal((uint)100, node.SymbolID);
        Assert.Equal((uint)200, node.NodeType);
        Assert.Equal((uint)300, node.SourceStart);
        Assert.Equal((uint)400, node.SourceLength);
        Assert.Equal((ulong)500, node.PackedNodesOffset);
        Assert.Equal((ulong)600, node.PropertiesOffset);
    }

    [Fact]
    public void PackedNodeDataV2_Size_Is24Bytes()
    {
        Assert.Equal(24, PackedNodeDataV2.SIZE);
        Assert.Equal(24, Marshal.SizeOf<PackedNodeDataV2>());
    }

    [Fact]
    public void PackedNodeDataV2_Fields_AreCorrect()
    {
        var node = new PackedNodeDataV2(
            ruleId: 100,
            childNodesOffset: 200,
            cpgEdgesOffset: 300,
            reserved: 0
        );

        Assert.Equal((uint)100, node.RuleID);
        Assert.Equal((ulong)200, node.ChildNodesOffset);
        Assert.Equal((ulong)300, node.CpgEdgesOffset);
        Assert.Equal((uint)0, node.Reserved);
    }

    // === Integration Tests for V2 Methodology ===

    [Fact]
    public void BuilderOptions_DefaultsToV1()
    {
        var options = new GraphBuilderOptions();
        Assert.Equal(SchemaVersion.V1, options.Schema);
    }

    [Fact]
    public void BuilderOptions_UniversalCreatesV2()
    {
        var options = GraphBuilderOptions.Universal();
        Assert.Equal(SchemaVersion.V2, options.Schema);
    }

    [Fact]
    public void BuilderOptions_CompactCreatesV1()
    {
        var options = GraphBuilderOptions.Compact();
        Assert.Equal(SchemaVersion.V1, options.Schema);
    }

    [Fact]
    public void Builder_WithV1Options_CreatesV1Graph()
    {
        // Arrange
        var options = GraphBuilderOptions.Compact();
        using var builder = new CognitiveGraphBuilder(options);
        
        // Act
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 11, null, null);
        var buffer = builder.Build(rootOffset, "Hello World");
        
        // Assert - Create graph and check it's V1
        using var graph = new CognitiveGraph(buffer);
        Assert.Equal(SchemaVersion.V1, graph.SchemaVersion);
    }

    [Fact]
    public void Builder_WithV2Options_CreatesV2Graph()
    {
        // Arrange
        var options = GraphBuilderOptions.Universal();
        using var builder = new CognitiveGraphBuilder(options);
        
        // Act
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 11, null, null);
        var buffer = builder.Build(rootOffset, "Hello World");
        
        // Assert - Create graph and check it's V2
        using var graph = new CognitiveGraph(buffer);
        Assert.Equal(SchemaVersion.V2, graph.SchemaVersion);
    }

    [Fact]
    public void CognitiveGraph_DetectsV1Schema()
    {
        // Arrange - Build a V1 graph
        using var builder = new CognitiveGraphBuilder(); // Default is V1
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 5, null, null);
        var buffer = builder.Build(rootOffset, "Hello");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        
        // Assert
        Assert.Equal(SchemaVersion.V1, graph.SchemaVersion);
        Assert.NotNull(graph.GetHeader());
        Assert.Null(graph.GetHeaderV2());
    }

    [Fact]
    public void CognitiveGraph_DetectsV2Schema()
    {
        // Arrange - Build a V2 graph
        var options = GraphBuilderOptions.Universal();
        using var builder = new CognitiveGraphBuilder(options);
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 5, null, null);
        var buffer = builder.Build(rootOffset, "Hello");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        
        // Assert
        Assert.Equal(SchemaVersion.V2, graph.SchemaVersion);
        Assert.Null(graph.GetHeader());
        Assert.NotNull(graph.GetHeaderV2());
    }

    [Fact]
    public void V1Graph_GetRootNode_Works()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootOffset = builder.WriteSymbolNode(10, 20, 0, 11, null, null);
        var buffer = builder.Build(rootOffset, "Hello World");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var rootNode = graph.GetRootNode();
        
        // Assert
        Assert.Equal((ushort)10, rootNode.SymbolID);
        Assert.Equal((ushort)20, rootNode.NodeType);
        Assert.Equal((uint)0, rootNode.SourceStart);
        Assert.Equal((uint)11, rootNode.SourceLength);
    }

    [Fact]
    public void V2Graph_GetRootNodeV2_Works()
    {
        // Arrange - V2 works best with file-based graphs
        var tempFile = Path.GetTempFileName();
        try
        {
            const string sourceText = "Hello World";
            var options = GraphBuilderOptions.Universal();
            
            using (var builder = new CognitiveGraphBuilder(options))
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(10, 20, 0, 11, null, null);
                builder.Build(stream, rootOffset, sourceText);
            }
            
            // Act
            using var graph = new CognitiveGraph(tempFile);
            var rootNode = graph.GetRootNodeV2();
            
            // Assert
            Assert.Equal((uint)10, rootNode.SymbolID);
            Assert.Equal((uint)20, rootNode.NodeType);
            Assert.Equal((uint)0, rootNode.SourceStart);
            Assert.Equal((uint)11, rootNode.SourceLength);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void V1Graph_GetRootNodeV2_ThrowsException()
    {
        // Arrange
        using var builder = new CognitiveGraphBuilder();
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 5, null, null);
        var buffer = builder.Build(rootOffset, "Hello");
        using var graph = new CognitiveGraph(buffer);
        
        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => graph.GetRootNodeV2());
    }

    [Fact]
    public void V2Graph_GetRootNode_ThrowsException()
    {
        // Arrange - Create file-based V2 graph
        var tempFile = Path.GetTempFileName();
        try
        {
            var options = GraphBuilderOptions.Universal();
            using (var builder = new CognitiveGraphBuilder(options))
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(1, 2, 0, 5, null, null);
                builder.Build(stream, rootOffset, "Hello");
            }
            
            using var graph = new CognitiveGraph(tempFile);
            
            // Act & Assert
            Assert.Throws<InvalidOperationException>(() => graph.GetRootNode());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void V1AndV2Graphs_GetSourceText_Works()
    {
        const string sourceText = "Hello World Test";
        
        // V1 Graph
        using var builderV1 = new CognitiveGraphBuilder();
        var rootOffsetV1 = builderV1.WriteSymbolNode(1, 2, 0, (uint)sourceText.Length, null, null);
        var bufferV1 = builderV1.Build(rootOffsetV1, sourceText);
        using var graphV1 = new CognitiveGraph(bufferV1);
        Assert.Equal(sourceText, graphV1.GetSourceText());
        
        // V2 Graph
        var optionsV2 = GraphBuilderOptions.Universal();
        using var builderV2 = new CognitiveGraphBuilder(optionsV2);
        var rootOffsetV2 = builderV2.WriteSymbolNode(1, 2, 0, (uint)sourceText.Length, null, null);
        var bufferV2 = builderV2.Build(rootOffsetV2, sourceText);
        using var graphV2 = new CognitiveGraph(bufferV2);
        Assert.Equal(sourceText, graphV2.GetSourceText());
    }

    [Fact]
    public void V2Graph_WriteToFile_AndReadBack_Works()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            const string sourceText = "Test Source Code";
            var options = GraphBuilderOptions.Universal();
            
            // Write V2 graph to file
            using (var builder = new CognitiveGraphBuilder(options))
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(5, 10, 0, (uint)sourceText.Length, null, null);
                builder.Build(stream, rootOffset, sourceText);
            }
            
            // Read V2 graph from file
            using var graph = new CognitiveGraph(tempFile);
            
            // Assert
            Assert.Equal(SchemaVersion.V2, graph.SchemaVersion);
            Assert.Equal(sourceText, graph.GetSourceText());
            var rootNode = graph.GetRootNodeV2();
            Assert.Equal((uint)5, rootNode.SymbolID);
            Assert.Equal((uint)10, rootNode.NodeType);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void V1Graph_WriteToFile_AndReadBack_Works()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            const string sourceText = "Test Source Code V1";
            
            // Write V1 graph to file
            using (var builder = new CognitiveGraphBuilder()) // Default V1
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(3, 7, 0, (uint)sourceText.Length, null, null);
                builder.Build(stream, rootOffset, sourceText);
            }
            
            // Read V1 graph from file
            using var graph = new CognitiveGraph(tempFile);
            
            // Assert
            Assert.Equal(SchemaVersion.V1, graph.SchemaVersion);
            Assert.Equal(sourceText, graph.GetSourceText());
            var rootNode = graph.GetRootNode();
            Assert.Equal((ushort)3, rootNode.SymbolID);
            Assert.Equal((ushort)7, rootNode.NodeType);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Upgrade_V1ToV2_CreatesV2Graph()
    {
        // Arrange
        var v1File = Path.GetTempFileName();
        var v2File = Path.GetTempFileName();
        
        try
        {
            const string sourceText = "Original V1 Source";
            
            // Create V1 graph
            using (var builder = new CognitiveGraphBuilder())
            using (var stream = new FileStream(v1File, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(1, 2, 0, (uint)sourceText.Length, null, null);
                builder.Build(stream, rootOffset, sourceText);
            }
            
            // Act - Upgrade V1 to V2
#pragma warning disable CS0618 // Type or member is obsolete
            CognitiveGraph.Upgrade(v1File, v2File);
#pragma warning restore CS0618
            
            // Assert - V2 file should exist and be V2 schema
            Assert.True(File.Exists(v2File));
            using var v2Graph = new CognitiveGraph(v2File);
            Assert.Equal(SchemaVersion.V2, v2Graph.SchemaVersion);
            Assert.Equal(sourceText, v2Graph.GetSourceText());
        }
        finally
        {
            if (File.Exists(v1File)) File.Delete(v1File);
            if (File.Exists(v2File)) File.Delete(v2File);
        }
    }

    [Fact]
    public void Upgrade_NonV1File_ThrowsException()
    {
        // Arrange
        var v2File = Path.GetTempFileName();
        var outputFile = Path.GetTempFileName();
        
        try
        {
            // Create a V2 graph
            var options = GraphBuilderOptions.Universal();
            using (var builder = new CognitiveGraphBuilder(options))
            using (var stream = new FileStream(v2File, FileMode.Create, FileAccess.Write))
            {
                var rootOffset = builder.WriteSymbolNode(1, 2, 0, 5, null, null);
                builder.Build(stream, rootOffset, "Hello");
            }
            
            // Act & Assert - Should throw because input is already V2
#pragma warning disable CS0618
            Assert.Throws<InvalidOperationException>(() => 
                CognitiveGraph.Upgrade(v2File, outputFile));
#pragma warning restore CS0618
        }
        finally
        {
            if (File.Exists(v2File)) File.Delete(v2File);
            if (File.Exists(outputFile)) File.Delete(outputFile);
        }
    }

    [Fact]
    public void V2Graph_GetStatistics_Works()
    {
        // Arrange
        var options = GraphBuilderOptions.Universal();
        using var builder = new CognitiveGraphBuilder(options);
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 12, null, null);
        var buffer = builder.Build(rootOffset, "Test Content");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        var stats = graph.GetStatistics();
        
        // Assert
        Assert.True(stats.BufferSize > 0);
        Assert.True(stats.SourceLength > 0);
    }

    [Fact]
    public void BackwardCompatibility_ExistingV1GraphsStillWork()
    {
        // This test ensures that the default behavior (V1) is maintained
        // for backward compatibility with existing code
        
        // Arrange - Create graph using old API (no options)
        using var builder = new CognitiveGraphBuilder();
        var rootOffset = builder.WriteSymbolNode(1, 2, 0, 4, null, null);
        var buffer = builder.Build(rootOffset, "Test");
        
        // Act
        using var graph = new CognitiveGraph(buffer);
        
        // Assert - Should be V1 for backward compatibility
        Assert.Equal(SchemaVersion.V1, graph.SchemaVersion);
        var rootNode = graph.GetRootNode(); // Old API should still work
        Assert.Equal((ushort)1, rootNode.SymbolID);
    }
}
