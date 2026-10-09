using System.Collections.Generic;
using Xunit;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph;

namespace CognitiveGraph.Tests;

/// <summary>
/// Consumer-contract tests (CognitiveGraph issue #53): stable node identity
/// across edit operations, identifier-level span fidelity distinct from
/// declaration spans, and the declaration-relationship CPG edge types
/// (INHERITS, IMPLEMENTS, USES_TYPE) needed for spec-tree extraction.
/// </summary>
public class ConsumerContractTests
{
    /// <summary>
    /// Builds a graph shaped like a declaration tree:
    /// root(file) -> class Customer (identifier span 14..22 inside a
    /// declaration span 0..46) with INHERITS -> Entity and IMPLEMENTS ->
    /// IDisposable edges.
    /// </summary>
    private static CognitiveGraph BuildDeclarationGraph()
    {
        using var builder = new CognitiveGraphBuilder();

        var entityOffset = builder.WriteSymbolNode(
            symbolId: 20,
            nodeType: 200,
            sourceStart: 60,
            sourceLength: 6,
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "Entity")
            });

        var disposableOffset = builder.WriteSymbolNode(
            symbolId: 21,
            nodeType: 201,
            sourceStart: 80,
            sourceLength: 13,
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "IDisposable")
            });

        var customerOffset = builder.WriteSymbolNode(
            symbolId: 10,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 46,
            properties: new List<(string key, PropertyValueType type, object value)>
            {
                ("Name", PropertyValueType.String, "Customer"),
                ("Kind", PropertyValueType.String, "class"),
                ("IdentifierStart", PropertyValueType.UInt32, 14u),
                ("IdentifierLength", PropertyValueType.UInt32, 8u)
            });

        var inheritsPropertiesOffset = (uint)builder.WritePropertyList(
            new List<(string key, PropertyValueType type, object value)>
            {
                ("relation", PropertyValueType.String, "base")
            });

        var classPackedOffset = builder.WritePackedNode(
            ruleId: 30,
            childNodeOffsets: new List<uint> { customerOffset },
            cpgEdges: new List<CpgEdgeData>
            {
                new((ushort)EdgeType.INHERITS, entityOffset, inheritsPropertiesOffset),
                new((ushort)EdgeType.IMPLEMENTS, disposableOffset, inheritsPropertiesOffset)
            });

        var rootOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 1,
            sourceStart: 0,
            sourceLength: 120,
            packedNodeOffsets: new List<uint> { classPackedOffset });

        var buffer = builder.Build(rootOffset, "public class Customer : Entity, IDisposable { }");
        return new CognitiveGraph(buffer);
    }

    [Fact]
    public void IdentifierSpan_DistinctFromDeclarationSpan()
    {
        using var graph = BuildDeclarationGraph();
        var root = graph.GetRootNode();

        var declarationNode = root.GetPackedNodes()[0].GetChildNodes()[0];

        // Declaration span covers the whole declaration...
        Assert.Equal(0u, declarationNode.SourceStart);
        Assert.Equal(46u, declarationNode.SourceLength);

        // ...while the identifier span (recorded per the consumer contract
        // as identifier properties) covers exactly the declared name.
        Assert.True(declarationNode.TryGetProperty("IdentifierStart", out var start));
        Assert.True(declarationNode.TryGetProperty("IdentifierLength", out var length));
        Assert.Equal(14u, start.AsUInt32());
        Assert.Equal(8u, length.AsUInt32());
        Assert.True(start.AsUInt32() >= declarationNode.SourceStart);
        Assert.True(start.AsUInt32() + length.AsUInt32() <= declarationNode.SourceStart + declarationNode.SourceLength);
    }

    [Fact]
    public void DeclarationEdges_CarryInheritsAndImplements()
    {
        using var graph = BuildDeclarationGraph();
        var root = graph.GetRootNode();

        var edges = root.GetPackedNodes()[0].GetCpgEdges();

        Assert.Equal(2, edges.Count);
        Assert.Equal(EdgeType.INHERITS, edges[0].EdgeType);
        Assert.Equal(20, edges[0].GetTargetNode().SymbolID);
        Assert.Equal(EdgeType.IMPLEMENTS, edges[1].EdgeType);
        Assert.Equal(21, edges[1].GetTargetNode().SymbolID);
        Assert.True(edges[0].TryGetProperty("relation", out var relation));
        Assert.Equal("base", relation.AsString());
    }

    [Fact]
    public void NodeIdentity_StableAcrossEditorClone()
    {
        using var original = BuildDeclarationGraph();

        using var editor = new CognitiveGraphEditor(original);
        using var clone = editor.Build();

        var originalRoot = original.GetRootNode();
        var cloneRoot = clone.GetRootNode();

        // Identity: same symbol ids, node types and spans after re-build
        Assert.Equal(originalRoot.SymbolID, cloneRoot.SymbolID);
        Assert.Equal(originalRoot.NodeType, cloneRoot.NodeType);
        Assert.Equal(originalRoot.SourceStart, cloneRoot.SourceStart);

        var originalChild = originalRoot.GetPackedNodes()[0].GetChildNodes()[0];
        var cloneChild = cloneRoot.GetPackedNodes()[0].GetChildNodes()[0];
        Assert.Equal(originalChild.SymbolID, cloneChild.SymbolID);
        Assert.Equal(originalChild.SourceStart, cloneChild.SourceStart);
        Assert.Equal(originalChild.SourceLength, cloneChild.SourceLength);

        // Edges survive the clone and still target the same identity
        var cloneEdges = cloneRoot.GetPackedNodes()[0].GetCpgEdges();
        Assert.Equal(EdgeType.INHERITS, cloneEdges[0].EdgeType);
        Assert.Equal(20, cloneEdges[0].GetTargetNode().SymbolID);
    }

    [Fact]
    public void DeclarationEdgeTypes_AreDistinctValues()
    {
        Assert.NotEqual(EdgeType.INHERITS, EdgeType.IMPLEMENTS);
        Assert.NotEqual(EdgeType.IMPLEMENTS, EdgeType.USES_TYPE);
        Assert.NotEqual(EdgeType.INHERITS, EdgeType.USES_TYPE);
        Assert.NotEqual(EdgeType.INHERITS, EdgeType.TYPE);
        Assert.Equal(6, (int)EdgeType.INHERITS);
        Assert.Equal(7, (int)EdgeType.IMPLEMENTS);
        Assert.Equal(8, (int)EdgeType.USES_TYPE);
    }
}
