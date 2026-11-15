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


using System.Text;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for the UTF-8-native <c>Build</c> overloads: a graph built
/// from UTF-8 source bytes must be byte-identical to the same graph
/// built from the equivalent string, for both V1 and V2 schemas and
/// for the file-stream persistence path, and the resulting graphs must
/// read back identically. The source text deliberately contains
/// multibyte UTF-8 (Danish letters, an em dash) so an encoding mistake
/// cannot hide behind ASCII-only input.
/// </summary>
public class Utf8BuildTests
{
    private const string SourceText = "håndværker æ ø å — 'quoted' + 42";

    /// <summary>
    /// Writes one symbol node with string and int properties so the
    /// graph carries a string table, a property list and an interval
    /// tree entry, not just a header.
    /// </summary>
    private static uint BuildSampleGraph(CognitiveGraphBuilder builder)
    {
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "BinaryExpression"),
            ("Operator", PropertyValueType.String, "+"),
            ("Count", PropertyValueType.Int32, 42)
        };

        return builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 200,
            sourceStart: 0,
            sourceLength: (uint)Encoding.UTF8.GetByteCount(SourceText),
            properties: properties);
    }

    [Fact]
    public void Utf8Build_V1_ProducesByteIdenticalBufferToStringBuild()
    {
        using var stringBuilder = new CognitiveGraphBuilder();
        using var bytesBuilder = new CognitiveGraphBuilder();

        var rootFromString = BuildSampleGraph(stringBuilder);
        var bufferFromString = stringBuilder.Build(rootFromString, SourceText);

        var rootFromBytes = BuildSampleGraph(bytesBuilder);
        var bufferFromBytes = bytesBuilder.Build(rootFromBytes, Encoding.UTF8.GetBytes(SourceText));

        Assert.Equal(bufferFromString.Length, bufferFromBytes.Length);
        Assert.True(
            bufferFromString.AsSpan().SequenceEqual(bufferFromBytes.AsSpan()),
            "The V1 graph built from UTF-8 bytes must be byte-identical to the string-built graph.");
    }

    [Fact]
    public void Utf8Build_V2_ProducesByteIdenticalBufferToStringBuild()
    {
        using var stringBuilder = new CognitiveGraphBuilder(GraphBuilderOptions.Universal());
        using var bytesBuilder = new CognitiveGraphBuilder(GraphBuilderOptions.Universal());

        var rootFromString = BuildSampleGraph(stringBuilder);
        var bufferFromString = stringBuilder.Build(rootFromString, SourceText);

        var rootFromBytes = BuildSampleGraph(bytesBuilder);
        var bufferFromBytes = bytesBuilder.Build(rootFromBytes, Encoding.UTF8.GetBytes(SourceText));

        Assert.Equal(bufferFromString.Length, bufferFromBytes.Length);
        Assert.True(
            bufferFromString.AsSpan().SequenceEqual(bufferFromBytes.AsSpan()),
            "The V2 graph built from UTF-8 bytes must be byte-identical to the string-built graph.");
    }

    [Fact]
    public void Utf8Build_V1_GraphReadsBackIdentically()
    {
        using var builder = new CognitiveGraphBuilder();
        var rootOffset = BuildSampleGraph(builder);
        var buffer = builder.Build(rootOffset, Encoding.UTF8.GetBytes(SourceText));

        using var graph = new CognitiveGraph(buffer);

        Assert.Equal(SchemaVersion.V1, graph.SchemaVersion);
        Assert.Equal(SourceText, graph.GetSourceText());

        var rootNode = graph.GetRootNode();
        Assert.Equal((ushort)1, rootNode.SymbolID);
        Assert.Equal((ushort)200, rootNode.NodeType);
        Assert.True(rootNode.TryGetProperty("Operator", out var op));
        Assert.Equal("+", op.AsString());
        Assert.True(rootNode.TryGetProperty("Count", out var count));
        Assert.Equal(42, count.AsInt32());
    }

    [Fact]
    public void Utf8Build_V2_GraphReadsBackIdentically()
    {
        using var builder = new CognitiveGraphBuilder(GraphBuilderOptions.Universal());
        var rootOffset = BuildSampleGraph(builder);
        var buffer = builder.Build(rootOffset, Encoding.UTF8.GetBytes(SourceText));

        using var graph = new CognitiveGraph(buffer);

        Assert.Equal(SchemaVersion.V2, graph.SchemaVersion);
        Assert.Equal(SourceText, graph.GetSourceText());
    }

    [Fact]
    public void Utf8Build_EmptySource_MatchesEmptyStringBuild()
    {
        using var stringBuilder = new CognitiveGraphBuilder();
        using var bytesBuilder = new CognitiveGraphBuilder();

        var rootFromString = BuildSampleGraph(stringBuilder);
        var bufferFromString = stringBuilder.Build(rootFromString, string.Empty);

        var rootFromBytes = BuildSampleGraph(bytesBuilder);
        var bufferFromBytes = bytesBuilder.Build(rootFromBytes, ReadOnlyMemory<byte>.Empty);

        Assert.Equal(bufferFromString.Length, bufferFromBytes.Length);
        Assert.True(bufferFromString.AsSpan().SequenceEqual(bufferFromBytes.AsSpan()));
    }

    [Fact]
    public void Utf8Build_FileStream_V1_WritesIdenticalFileToStringBuild()
    {
        var stringFile = Path.GetTempFileName();
        var bytesFile = Path.GetTempFileName();

        try
        {
            using (var stringBuilder = new CognitiveGraphBuilder())
            using (var bytesBuilder = new CognitiveGraphBuilder())
            {
                var rootFromString = BuildSampleGraph(stringBuilder);
                using (var stream = File.Create(stringFile))
                {
                    stringBuilder.Build(stream, rootFromString, SourceText);
                }

                var rootFromBytes = BuildSampleGraph(bytesBuilder);
                using (var stream = File.Create(bytesFile))
                {
                    bytesBuilder.Build(stream, rootFromBytes, Encoding.UTF8.GetBytes(SourceText));
                }
            }

            Assert.True(
                File.ReadAllBytes(stringFile).SequenceEqual(File.ReadAllBytes(bytesFile)),
                "The file written from UTF-8 bytes must be byte-identical to the string-built file.");

            using var graph = new CognitiveGraph(bytesFile);
            Assert.Equal(SourceText, graph.GetSourceText());
        }
        finally
        {
            if (File.Exists(stringFile)) File.Delete(stringFile);
            if (File.Exists(bytesFile)) File.Delete(bytesFile);
        }
    }

    [Fact]
    public void Utf8Build_FileStream_V2_WritesIdenticalFileToStringBuild()
    {
        var stringFile = Path.GetTempFileName();
        var bytesFile = Path.GetTempFileName();

        try
        {
            using (var stringBuilder = new CognitiveGraphBuilder(GraphBuilderOptions.Universal()))
            using (var bytesBuilder = new CognitiveGraphBuilder(GraphBuilderOptions.Universal()))
            {
                var rootFromString = BuildSampleGraph(stringBuilder);
                using (var stream = File.Create(stringFile))
                {
                    stringBuilder.Build(stream, rootFromString, SourceText);
                }

                var rootFromBytes = BuildSampleGraph(bytesBuilder);
                using (var stream = File.Create(bytesFile))
                {
                    bytesBuilder.Build(stream, rootFromBytes, Encoding.UTF8.GetBytes(SourceText));
                }
            }

            Assert.True(
                File.ReadAllBytes(stringFile).SequenceEqual(File.ReadAllBytes(bytesFile)),
                "The V2 file written from UTF-8 bytes must be byte-identical to the string-built file.");
        }
        finally
        {
            if (File.Exists(stringFile)) File.Delete(stringFile);
            if (File.Exists(bytesFile)) File.Delete(bytesFile);
        }
    }
}
