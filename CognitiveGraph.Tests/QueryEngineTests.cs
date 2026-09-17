
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CognitiveGraph;
using CognitiveGraph.QueryEngine;

namespace CognitiveGraph.Tests;

/// <summary>
/// Tests for the GraphQL query engine.
/// All test graphs are multi-node (root + packed node + children) so queries
/// are exercised against non-root nodes.
/// </summary>
public class QueryEngineTests
{
    /// <summary>
    /// Builds a multi-node graph over "hello world foo":
    ///   root (Expression, symbol 10, type 1)
    ///     - packed node rule 7 with children: hello (symbol 1, type 100), world (symbol 2, type 101)
    ///     - packed node rule 8 with children: hello (shared), foo (symbol 3, type 102)
    /// The hello node is shared between both derivations (SPPF sharing) and carries properties.
    /// </summary>
    private sealed class MultiNodeGraph : IDisposable
    {
        public CognitiveGraph Graph = null!;
        public uint RootOffset;
        public uint HelloOffset;
        public uint WorldOffset;
        public uint FooOffset;

        public void Dispose() => Graph.Dispose();
    }

    private static MultiNodeGraph BuildMultiNodeGraph()
    {
        using var builder = new CognitiveGraphBuilder();

        var helloOffset = builder.WriteSymbolNode(
            symbolId: 1,
            nodeType: 100,
            sourceStart: 0,
            sourceLength: 5,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "hello"),
                ("Length", PropertyValueType.Int32, 5)
            });

        var worldOffset = builder.WriteSymbolNode(
            symbolId: 2,
            nodeType: 101,
            sourceStart: 6,
            sourceLength: 5,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "world")
            });

        var fooOffset = builder.WriteSymbolNode(
            symbolId: 3,
            nodeType: 102,
            sourceStart: 12,
            sourceLength: 3,
            properties: new List<(string, PropertyValueType, object)>
            {
                ("Name", PropertyValueType.String, "foo")
            });

        var derivationA = builder.WritePackedNode(ruleId: 7, childNodeOffsets: new List<uint> { helloOffset, worldOffset });
        var derivationB = builder.WritePackedNode(ruleId: 8, childNodeOffsets: new List<uint> { helloOffset, fooOffset });

        var rootOffset = builder.WriteSymbolNode(
            symbolId: 10,
            nodeType: 1,
            sourceStart: 0,
            sourceLength: 15,
            packedNodeOffsets: new List<uint> { derivationA, derivationB });

        var buffer = builder.Build(rootOffset, "hello world foo");
        return new MultiNodeGraph
        {
            Graph = new CognitiveGraph(buffer),
            RootOffset = rootOffset,
            HelloOffset = helloOffset,
            WorldOffset = worldOffset,
            FooOffset = fooOffset
        };
    }

    [Fact]
    public async Task QueryAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_WithNullQuery_ReturnsEmptyList()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync(null!);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_RootQuery_ReturnsRootNodeOffset()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ root { offset symbolId } }");

        // Assert
        var offset = Assert.Single(results);
        Assert.Equal(g.RootOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_SymbolIdFilter_MatchesNonRootNodes()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act - filter by symbolId matching a child, not the root
        var results = await g.Graph.QueryAsync("{ nodes(symbolId: 2) { offset symbolId } }");

        // Assert
        var offset = Assert.Single(results);
        Assert.Equal(g.WorldOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_SharedNode_MatchesOncePerQuery()
    {
        // Arrange - hello (symbolId 1) is shared by both derivations
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ nodes(symbolId: 1) { offset } }");

        // Assert - node enumeration is de-duplicated by offset
        var offset = Assert.Single(results);
        Assert.Equal(g.HelloOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_NodeTypeFilter_MatchesNonRootNodes()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act - filter by nodeType matching a child, not the root
        var results = await g.Graph.QueryAsync("{ nodes(nodeType: 102) { offset } }");

        // Assert
        var offset = Assert.Single(results);
        Assert.Equal(g.FooOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_NonMatchingFilters_ReturnsEmptyList()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ nodes(symbolId: 999) { offset } }");

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task QueryAsync_NodeLookupByOffset_ReturnsThatNode()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync($"{{ node(offset: {g.WorldOffset}) {{ offset symbolId sourceText }} }}");

        // Assert
        var offset = Assert.Single(results);
        Assert.Equal(g.WorldOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_ChildrenTraversal_ReturnsAllChildOffsets()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ root { children { offset } } }");

        // Assert - children across both derivations: hello, world (rule 7), hello, foo (rule 8)
        Assert.Equal(4, results.Count);
        Assert.Equal(g.HelloOffset, results[0]);
        Assert.Equal(g.WorldOffset, results[1]);
        Assert.Equal(g.HelloOffset, results[2]);
        Assert.Equal(g.FooOffset, results[3]);
    }

    [Fact]
    public async Task QueryAsync_PackedNodes_ExposeRulesAndChildren()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ root { packedNodes { ruleId children { offset } } } }");

        // Assert - all children of both derivations are returned
        Assert.Equal(4, results.Count);
        // First derivation (rule 7): hello + world
        Assert.Equal(g.HelloOffset, results[0]);
        Assert.Equal(g.WorldOffset, results[1]);
    }

    [Fact]
    public async Task QueryAsync_Properties_AreQueryable()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act - select nodes with their properties; offsets of all matched nodes returned
        var results = await g.Graph.QueryAsync("{ nodes(symbolId: 1) { offset properties { key value type } } }");

        // Assert
        var offset = Assert.Single(results);
        Assert.Equal(g.HelloOffset, offset);
    }

    [Fact]
    public async Task QueryAsync_SpatialLookup_FindsNonRootNodes()
    {
        // Arrange - "world" occupies source bytes 6..10
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ nodesAt(point: 8) { offset symbolId } }");

        // Assert - both the root and the world child contain byte 8
        Assert.Contains(g.WorldOffset, results);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsStructuredResultWithErrors()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();
        var engine = new GraphQLQueryEngine(g.Graph);

        // Act - unknown field fails validation
        var result = await engine.ExecuteAsync("{ root { bogusField } }");

        // Assert
        Assert.NotNull(result.Errors);
        Assert.True(result.Errors.Count > 0);
    }

    [Fact]
    public async Task QueryAsync_WithInvalidQuery_ThrowsInvalidOperationException()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();

        // Act & Assert - a real engine reports invalid queries instead of
        // silently returning the root node
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => g.Graph.QueryAsync("this is not graphql"));
    }

    [Fact]
    public async Task QueryAsync_MultipleQueries_ConsistentResults()
    {
        // Arrange
        using var g = BuildMultiNodeGraph();
        var query = "{ nodes(symbolId: 2) { offset } }";

        // Act
        var results1 = await g.Graph.QueryAsync(query);
        var results2 = await g.Graph.QueryAsync(query);
        var results3 = await g.Graph.QueryAsync(query);

        // Assert - the lazily built schema is reused safely across executions
        Assert.Equal(results1.Count, results2.Count);
        Assert.Equal(results1.Count, results3.Count);
        for (int i = 0; i < results1.Count; i++)
        {
            Assert.Equal(results1[i], results2[i]);
            Assert.Equal(results1[i], results3[i]);
        }

        Assert.Equal(g.WorldOffset, results1.Single());
    }

    [Fact]
    public async Task QueryAsync_UnfilteredNodes_ReturnsEveryNodeOnce()
    {
        // Arrange - 4 distinct nodes: root, hello, world, foo
        using var g = BuildMultiNodeGraph();

        // Act
        var results = await g.Graph.QueryAsync("{ nodes { offset } }");

        // Assert
        Assert.Equal(4, results.Count);
        Assert.Equal(4, results.Distinct().Count());
    }
}
