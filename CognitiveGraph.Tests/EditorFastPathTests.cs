/*
 * CognitiveGraph - Zero-Copy Cognitive Graph for Advanced Code Analysis
 * Copyright (C) 2024 DevelApp-ai
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
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
/// Tests for the editor's copy-on-write fast path (issue #68):
/// layout-preserving operations (ReplaceNode without packed-node/property
/// overrides, MoveNode, same-length UpdateProperty) are patched into a single
/// clone of the source buffer with all offsets preserved, instead of
/// triggering a full node-by-node rebuild. Layout-changing operations must
/// still fall back to the rebuild and produce correct results.
/// </summary>
public class EditorFastPathTests
{
    /// <summary>
    /// Builds a graph with a root (three string properties, deliberately
    /// equal byte lengths so payload patches never change layout), a child
    /// node with numeric/boolean/binary/string properties, two packed nodes
    /// sharing the child (SPPF sharing) and a CPG edge. Returns the captured
    /// node offsets for direct editor targeting.
    /// </summary>
    private static CognitiveGraph BuildGraph(out uint rootOffset, out uint childOffset)
    {
        using var builder = new CognitiveGraphBuilder();

        childOffset = builder.WriteSymbolNode(
            symbolId: 7,
            nodeType: 107,
            sourceStart: 5,
            sourceLength: 3,
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "aaaa"),
                ("Count", PropertyValueType.Int32, 9),
                ("Big", PropertyValueType.Int64, 1234567890123L),
                ("UBig", PropertyValueType.UInt64, 9876543210UL),
                ("Ratio", PropertyValueType.Float, 1.5f),
                ("Precise", PropertyValueType.Double, 3.25),
                ("Flag", PropertyValueType.Boolean, false),
                ("Blob", PropertyValueType.Binary, new byte[] { 1, 2, 3, 4 }),
                ("UCount", PropertyValueType.UInt32, 42u)
            });

        var firstPackedOffset = builder.WritePackedNode(
            ruleId: 10,
            childNodeOffsets: new List<uint> { childOffset },
            cpgEdges: new List<CpgEdgeData>());

        var secondPackedOffset = builder.WritePackedNode(11, new List<uint> { childOffset });

        rootOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 10,
            packedNodeOffsets: new List<uint> { firstPackedOffset, secondPackedOffset },
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "aaaa"),
                ("Value", PropertyValueType.String, "bbbb"),
                ("Extra", PropertyValueType.String, "cccc")
            });

        var buffer = builder.Build(rootOffset, "test source");
        return new CognitiveGraph(buffer);
    }

    [Fact]
    public void FastPath_ReplaceNode_WithoutOverrides_PatchesInPlace()
    {
        using var source = BuildGraph(out var rootOffset, out var childOffset);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .ReplaceNode(rootOffset, 2, 101, 0, 10) // same span, no overrides
            .Build();

        // Patched record values
        var root = result.GetNodeAt(rootOffset);
        Assert.Equal(2, root.SymbolID);
        Assert.Equal(101, root.NodeType);
        Assert.Equal(0u, root.SourceStart);
        Assert.Equal(10u, root.SourceLength);

        // Offsets preserved: the child is still at its original offset
        var child = result.GetNodeAt(childOffset);
        Assert.Equal(7, child.SymbolID);

        // Structure preserved: two derivations, shared child
        var packed = result.GetNodeAt(rootOffset).GetPackedNodes();
        Assert.Equal(2, packed.Count);
        Assert.Equal(10, packed[0].RuleID);
        Assert.Equal(11, packed[1].RuleID);
        Assert.Equal(childOffset, packed[0].GetChildNodes()[0].Offset);
        Assert.Equal(childOffset, packed[1].GetChildNodes()[0].Offset);

        Assert.Equal("test source", result.GetSourceText());
    }

    [Fact]
    public void FastPath_MoveNode_UpdatesNodeRecordAndIntervalTree()
    {
        using var source = BuildGraph(out _, out var childOffset);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .MoveNode(childOffset, 20, 3) // 5..7 -> 20..22
            .Build();

        // Node record patched
        var child = result.GetNodeAt(childOffset);
        Assert.Equal(20u, child.SourceStart);
        Assert.Equal(3u, child.SourceLength);

        // Interval tree rewritten: the new span finds the (same) node offset,
        // the old span no longer does.
        Assert.Contains(childOffset, result.FindNodesAt(21));
        Assert.DoesNotContain(childOffset, result.FindNodesAt(6));
    }

    [Fact]
    public void FastPath_ReplaceNode_WithNewSpan_UpdatesIntervalTree()
    {
        using var source = BuildGraph(out _, out var childOffset);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .ReplaceNode(childOffset, 7, 107, 40, 5) // 5..7 -> 40..44, no overrides
            .Build();

        var child = result.GetNodeAt(childOffset);
        Assert.Equal(40u, child.SourceStart);
        Assert.Equal(5u, child.SourceLength);
        Assert.Contains(childOffset, result.FindNodesAt(42));
        Assert.DoesNotContain(childOffset, result.FindNodesAt(6));
    }

    [Fact]
    public void FastPath_UpdateProperty_SameLengthPayload_PatchesEveryValueType()
    {
        using var source = BuildGraph(out var rootOffset, out var childOffset);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .UpdateProperty(childOffset, "Name", PropertyValueType.String, "zzzz")
            .UpdateProperty(childOffset, "Count", PropertyValueType.Int32, -77)
            .UpdateProperty(childOffset, "Big", PropertyValueType.Int64, -1234567890123L)
            .UpdateProperty(childOffset, "UBig", PropertyValueType.UInt64, 1234567890UL)
            .UpdateProperty(childOffset, "Ratio", PropertyValueType.Float, -2.5f)
            .UpdateProperty(childOffset, "Precise", PropertyValueType.Double, -7.75)
            .UpdateProperty(childOffset, "Flag", PropertyValueType.Boolean, true)
            .UpdateProperty(childOffset, "Blob", PropertyValueType.Binary, new byte[] { 9, 8, 7, 6 })
            .UpdateProperty(childOffset, "UCount", PropertyValueType.UInt32, 7u)
            .UpdateProperty(rootOffset, "Value", PropertyValueType.String, "dddd")
            .Build();

        var child = result.GetNodeAt(childOffset);
        Assert.True(child.TryGetProperty("Name", out var name));
        Assert.Equal("zzzz", name.AsString());
        Assert.True(child.TryGetProperty("Count", out var count));
        Assert.Equal(-77, count.AsInt32());
        Assert.True(child.TryGetProperty("Big", out var big));
        Assert.Equal(-1234567890123L, big.AsInt64());
        Assert.True(child.TryGetProperty("UBig", out var ubig));
        Assert.Equal(1234567890UL, ubig.AsUInt64());
        Assert.True(child.TryGetProperty("Ratio", out var ratio));
        Assert.Equal(-2.5f, ratio.AsFloat());
        Assert.True(child.TryGetProperty("Precise", out var precise));
        Assert.Equal(-7.75, precise.AsDouble());
        Assert.True(child.TryGetProperty("Flag", out var flag));
        Assert.True(flag.AsBoolean());
        Assert.True(child.TryGetProperty("Blob", out var blob));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, blob.AsBinary().ToArray());
        Assert.True(child.TryGetProperty("UCount", out var ucount));
        Assert.Equal(7u, ucount.AsUInt32());

        var root = result.GetNodeAt(rootOffset);
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal("dddd", value.AsString());
    }

    /// <summary>
    /// The in-place patch keeps each property at its original slot: the
    /// patched key stays in the middle of the list. The old rebuild semantics
    /// removed and re-appended the key, moving it to the end. Asserting the
    /// unchanged order also proves the fast path was actually taken.
    /// </summary>
    [Fact]
    public void FastPath_UpdateProperty_PreservesPropertyOrder()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .UpdateProperty(rootOffset, "Value", PropertyValueType.String, "dddd") // same byte length
            .Build();

        var properties = result.GetNodeAt(rootOffset).GetProperties();
        Assert.Equal(3, properties.Count);
        Assert.Equal("Name", properties[0].GetKey());
        Assert.Equal("Value", properties[1].GetKey());
        Assert.Equal("Extra", properties[2].GetKey());
    }

    [Fact]
    public void FastPath_UpdateProperty_LengthChange_FallsBackAndSucceeds()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .UpdateProperty(rootOffset, "Value", PropertyValueType.String, "a much longer value")
            .Build();

        var root = result.GetRootNode();
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal("a much longer value", value.AsString());
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("aaaa", name.AsString());
    }

    [Fact]
    public void FastPath_UpdateProperty_NewKey_FallsBackAndAddsProperty()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .UpdateProperty(rootOffset, "NewProperty", PropertyValueType.Boolean, true)
            .Build();

        var root = result.GetRootNode();
        Assert.True(root.TryGetProperty("NewProperty", out var property));
        Assert.True(property.AsBoolean());
    }

    [Fact]
    public void FastPath_UpdateProperty_TypeChange_FallsBackAndSucceeds()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .UpdateProperty(rootOffset, "Value", PropertyValueType.Int32, 123)
            .Build();

        var root = result.GetRootNode();
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal(123, value.AsInt32());
    }

    [Fact]
    public void FastPath_RemoveProperty_FallsBackAndRemovesProperty()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .RemoveProperty(rootOffset, "Value")
            .Build();

        var root = result.GetRootNode();
        Assert.False(root.TryGetProperty("Value", out _));
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("aaaa", name.AsString());
    }

    [Fact]
    public void FastPath_InsertAndDelete_FallBackAndSucceed()
    {
        using var source = BuildGraph(out var rootOffset, out var childOffset);

        var insertedOffset = 999999u; // stand-alone insert target offset
        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .InsertNode(insertedOffset, 42, 142, 30, 4,
                properties: new List<(string key, PropertyValueType type, object value)>
                {
                    ("Tag", PropertyValueType.String, "new")
                })
            .DeleteNode(childOffset)
            .Build();

        var root = result.GetRootNode();
        var packedNodes = root.GetPackedNodes();

        // The deleted child is dropped from both derivations
        Assert.Equal(2, packedNodes.Count);
        Assert.Equal(0, packedNodes[0].GetChildNodes().Count);
        Assert.Equal(0, packedNodes[1].GetChildNodes().Count);

        // The inserted node is present with its properties. Its offset in the
        // rebuilt graph is assigned by the builder; find it via its span.
        var matches = result.FindNodesAt(31);
        var inserted = Assert.Single(matches);
        var insertedNode = result.GetNodeAt(inserted);
        Assert.Equal(42, insertedNode.SymbolID);
        Assert.True(insertedNode.TryGetProperty("Tag", out var tag));
        Assert.Equal("new", tag.AsString());

        // The root survives untouched
        Assert.True(root.TryGetProperty("Name", out var name));
        Assert.Equal("aaaa", name.AsString());
    }

    [Fact]
    public void FastPath_MixedQueue_WithLayoutChangingOp_FallsBackForWholeQueue()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using var result = editor
            .MoveNode(rootOffset, 100, 10)                       // patchable
            .UpdateProperty(rootOffset, "New", PropertyValueType.Int32, 5) // not patchable
            .Build();

        // Both operations applied by the rebuild
        var root = result.GetRootNode();
        Assert.Equal(100u, root.SourceStart);
        Assert.True(root.TryGetProperty("New", out var value));
        Assert.Equal(5, value.AsInt32());
    }

    [Fact]
    public void FastPath_RepeatedEdits_RemainCorrectAndStable()
    {
        using var source = BuildGraph(out var rootOffset, out var childOffset);

        var current = source;
        var owned = false;
        try
        {
            for (var i = 0; i < 50; i++)
            {
                using var editor = new CognitiveGraphEditor(current);
                var next = editor
                    .UpdateProperty(childOffset, "Count", PropertyValueType.Int32, i)
                    .MoveNode(childOffset, (uint)(100 + i * 10), 3)
                    .Build();

                // Offsets stay stable across every fast-path edit
                Assert.Equal(rootOffset, next.GetHeader()!.Value.RootNodeOffset);
                var child = next.GetNodeAt(childOffset);
                Assert.Equal((uint)(100 + i * 10), child.SourceStart);
                Assert.True(child.TryGetProperty("Count", out var count));
                Assert.Equal(i, count.AsInt32());
                Assert.Contains(childOffset, next.FindNodesAt((uint)(101 + i * 10)));
                Assert.DoesNotContain(childOffset, next.FindNodesAt(6));

                if (owned)
                    current.Dispose();
                current = next;
                owned = true;
            }
        }
        finally
        {
            if (owned)
                current.Dispose();
        }
    }

    [Fact]
    public void FastPath_DoesNotModifySourceGraph()
    {
        using var source = BuildGraph(out var rootOffset, out var childOffset);

        using (var editor = new CognitiveGraphEditor(source))
        using (editor
            .ReplaceNode(rootOffset, 2, 101, 50, 8)
            .UpdateProperty(childOffset, "Count", PropertyValueType.Int32, -1)
            .MoveNode(childOffset, 200, 3)
            .Build())
        {
        }

        // The source graph is untouched (copy-on-write)
        var sourceRoot = source.GetNodeAt(rootOffset);
        Assert.Equal(1, sourceRoot.SymbolID);
        Assert.Equal(100, sourceRoot.NodeType);
        Assert.Equal(0u, sourceRoot.SourceStart);

        var sourceChild = source.GetNodeAt(childOffset);
        Assert.Equal(5u, sourceChild.SourceStart);
        Assert.True(sourceChild.TryGetProperty("Count", out var count));
        Assert.Equal(9, count.AsInt32());
        Assert.DoesNotContain(childOffset, source.FindNodesAt(201));
        Assert.Contains(childOffset, source.FindNodesAt(6));
    }

    [Fact]
    public void FastPath_EditorCanBeRebuiltAfterFastPathBuild()
    {
        using var source = BuildGraph(out var rootOffset, out _);

        using var editor = new CognitiveGraphEditor(source);
        using (editor.UpdateProperty(rootOffset, "Value", PropertyValueType.String, "dddd").Build())
        {
        }

        // Second Build on the same editor must not be corrupted by leftover
        // fast-path state; it produces the same patch again from the source.
        using var second = editor
            .UpdateProperty(rootOffset, "Value", PropertyValueType.String, "eeee")
            .Build();

        var root = second.GetNodeAt(rootOffset);
        Assert.True(root.TryGetProperty("Value", out var value));
        Assert.Equal("eeee", value.AsString());
    }
}
