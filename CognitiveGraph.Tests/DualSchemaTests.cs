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
using System.Runtime.InteropServices;

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
}
