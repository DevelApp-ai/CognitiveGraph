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

using System.Linq;
using GraphQL;
using GraphQL.Types;

namespace CognitiveGraph.QueryEngine;

/// <summary>
/// GraphQL schema over a CognitiveGraph: the query root exposes the parse-tree
/// root, offset-based node lookup, whole-tree node search with filters, and
/// spatial (interval-tree) lookup.
/// </summary>
public sealed class CognitiveGraphSchema : GraphQL.Types.Schema
{
    public CognitiveGraphSchema(CognitiveGraph graph)
        : base(new GraphTypeProvider(graph))
    {
        Query = new GraphQueryType(graph);
    }

    /// <summary>
    /// Supplies the schema's graph types. The engine-backed types carry a
    /// CognitiveGraph reference, so they cannot be created via Activator;
    /// each is instantiated once and cached. All other types (scalars, list
    /// wrappers) fall back to Activator, matching DefaultServiceProvider.
    /// </summary>
    private sealed class GraphTypeProvider : IServiceProvider
    {
        private readonly CognitiveGraph _graph;
        private readonly Dictionary<Type, object> _instances = new();

        public GraphTypeProvider(CognitiveGraph graph)
        {
            _graph = graph;
        }

        public object? GetService(Type serviceType)
        {
            if (_instances.TryGetValue(serviceType, out var cached))
                return cached;

            object? instance = serviceType switch
            {
                _ when serviceType == typeof(NodeGraphType) => new NodeGraphType(_graph),
                _ when serviceType == typeof(PackedNodeGraphType) => new PackedNodeGraphType(_graph),
                _ when serviceType == typeof(EdgeGraphType) => new EdgeGraphType(_graph),
                _ when serviceType == typeof(NodePropertyGraphType) => new NodePropertyGraphType(),
                // Interfaces/abstract types (e.g. IEnumerable<IConfigureSchema> probed by the
                // Schema constructor) must yield null rather than throw; the caller falls back
                // to its built-ins or defaults.
                _ when serviceType.IsInterface || serviceType.IsAbstract => null,
                _ => Activator.CreateInstance(serviceType),
            };

            if (instance != null)
                _instances[serviceType] = instance;

            return instance;
        }
    }
}

/// <summary>
/// Root query type:
///   root                  - the parse-tree root node
///   node(offset)          - node lookup by buffer offset
///   nodes(symbolId, nodeType) - every node reachable from the root, optionally filtered
///   nodesAt(point)        - nodes whose source span contains the byte offset
/// </summary>
internal sealed class GraphQueryType : ObjectGraphType
{
    public GraphQueryType(CognitiveGraph graph)
    {
        Name = "CognitiveGraphQuery";

        Field<NodeGraphType>(
            "root",
            "The root symbol node of the parse tree.",
            resolve: context => GraphTraversal.Snapshot(graph, graph.GetRootNode().Offset));

        Field<NodeGraphType>(
            "node",
            "Looks up a symbol node by its buffer offset.",
            arguments: new QueryArguments(
                new QueryArgument<LongGraphType>
                {
                    Name = "offset",
                    Description = "Absolute byte offset of the symbol node in the graph buffer.",
                }),
            resolve: context =>
            {
                var offset = context.GetArgument<long>("offset");
                if (offset < 0 || offset > uint.MaxValue)
                    throw new GraphQL.ExecutionError($"Node offset {offset} is outside the V1 address space.");

                return GraphTraversal.Snapshot(graph, (uint)offset);
            });

        Field<ListGraphType<NodeGraphType>>(
            "nodes",
            "All symbol nodes reachable from the root, optionally filtered by symbol id or node type.",
            arguments: new QueryArguments(
                new QueryArgument<IntGraphType>
                {
                    Name = "symbolId",
                    Description = "Match only nodes with this grammar symbol id.",
                },
                new QueryArgument<IntGraphType>
                {
                    Name = "nodeType",
                    Description = "Match only nodes with this semantic node type.",
                }),
            resolve: context =>
            {
                var symbolId = context.GetArgument<int?>("symbolId");
                var nodeType = context.GetArgument<int?>("nodeType");

                return GraphTraversal.GetAllNodes(graph).Where(node =>
                    (symbolId == null || node.SymbolId == symbolId) &&
                    (nodeType == null || node.NodeType == nodeType));
            });

        Field<ListGraphType<NodeGraphType>>(
            "nodesAt",
            "All symbol nodes whose source span contains the given byte offset (spatial index lookup).",
            arguments: new QueryArguments(
                new QueryArgument<LongGraphType>
                {
                    Name = "point",
                    Description = "Byte offset into the source text.",
                }),
            resolve: context =>
            {
                var point = context.GetArgument<long>("point");
                if (point < 0 || point > uint.MaxValue)
                    throw new GraphQL.ExecutionError($"Source offset {point} is outside the V1 address space.");

                return graph.FindNodesAt((uint)point)
                    .Select(offset => GraphTraversal.Snapshot(graph, offset));
            });
    }
}

/// <summary>
/// A symbol node with its parse-tree structure (packed nodes, children) and properties.
/// </summary>
internal sealed class NodeGraphType : ObjectGraphType<NodeInfo>
{
    public NodeGraphType(CognitiveGraph graph)
    {
        Name = "Node";

        Field<LongGraphType>(
            "offset",
            "Absolute byte offset of this node in the graph buffer.",
            resolve: context => (long)context.Source.Offset);

        Field<IntGraphType>(
            "symbolId",
            "Grammar symbol id (terminal/non-terminal).",
            resolve: context => context.Source.SymbolId);

        Field<IntGraphType>(
            "nodeType",
            "Semantic node type.",
            resolve: context => context.Source.NodeType);

        Field<LongGraphType>(
            "sourceStart",
            "Start byte offset of this node's span in the source text.",
            resolve: context => (long)context.Source.SourceStart);

        Field<LongGraphType>(
            "sourceLength",
            "Length of this node's span in the source text.",
            resolve: context => (long)context.Source.SourceLength);

        Field<BooleanGraphType>(
            "isAmbiguous",
            "True if this node has more than one derivation (SPPF ambiguity).",
            resolve: context => context.Source.IsAmbiguous);

        Field<StringGraphType>(
            "sourceText",
            "The source text covered by this node.",
            resolve: context => GraphTraversal.SourceText(graph, context.Source.Offset));

        Field<ListGraphType<NodePropertyGraphType>>(
            "properties",
            "Key/value properties attached to this node.",
            resolve: context => GraphTraversal.GetProperties(graph, context.Source.Offset));

        Field<ListGraphType<PackedNodeGraphType>>(
            "packedNodes",
            "Derivation alternatives (SPPF packed nodes) of this symbol.",
            resolve: context => GraphTraversal.GetPackedNodes(graph, context.Source.Offset));

        Field<ListGraphType<NodeGraphType>>(
            "children",
            "Child symbol nodes across all derivations of this symbol.",
            resolve: context => GraphTraversal.GetChildren(graph, context.Source.Offset));
    }
}

/// <summary>
/// A key/value property. The value is rendered as a string; `type` names the
/// stored value type (String, Int32, ..., Binary).
/// </summary>
internal sealed class NodePropertyGraphType : ObjectGraphType<NodeProperty>
{
    public NodePropertyGraphType()
    {
        Name = "Property";

        Field<StringGraphType>(
            "key",
            resolve: context => context.Source.Key);

        Field<StringGraphType>(
            "type",
            resolve: context => context.Source.Type);

        Field<StringGraphType>(
            "value",
            resolve: context => context.Source.Value);
    }
}

/// <summary>
/// One derivation alternative of an ambiguous symbol node.
/// </summary>
internal sealed class PackedNodeGraphType : ObjectGraphType<PackedNodeInfo>
{
    public PackedNodeGraphType(CognitiveGraph graph)
    {
        Name = "PackedNode";

        Field<LongGraphType>(
            "offset",
            "Absolute byte offset of this packed node in the graph buffer.",
            resolve: context => (long)context.Source.Offset);

        Field<IntGraphType>(
            "ruleId",
            "Grammar rule applied by this derivation.",
            resolve: context => context.Source.RuleId);

        Field<ListGraphType<NodeGraphType>>(
            "children",
            "Child symbol nodes of this derivation.",
            resolve: context => GraphTraversal.GetChildrenOfPackedNode(
                graph, context.Source.OwnerSymbolOffset, context.Source.Offset));

        Field<ListGraphType<EdgeGraphType>>(
            "cpgEdges",
            "CPG edges (control/data flow) attached to this derivation.",
            resolve: context => GraphTraversal.GetEdgesOfPackedNode(
                graph, context.Source.OwnerSymbolOffset, context.Source.Offset));
    }
}

/// <summary>
/// A CPG edge from a packed node derivation to a target symbol node.
/// </summary>
internal sealed class EdgeGraphType : ObjectGraphType<EdgeInfo>
{
    public EdgeGraphType(CognitiveGraph graph)
    {
        Name = "CpgEdge";

        Field<IntGraphType>(
            "edgeType",
            resolve: context => context.Source.EdgeType);

        Field<LongGraphType>(
            "targetOffset",
            resolve: context => (long)context.Source.TargetNodeOffset);

        Field<NodeGraphType>(
            "target",
            "The symbol node this edge points to.",
            resolve: context => GraphTraversal.Snapshot(graph, context.Source.TargetNodeOffset));

        Field<ListGraphType<NodePropertyGraphType>>(
            "properties",
            "Properties attached to this edge (e.g. CFG condition).",
            resolve: context => GraphTraversal.GetEdgeProperties(
                graph, context.Source.OwnerSymbolOffset, context.Source.OwnerPackedNodeOffset, context.Source.TargetNodeOffset));
    }
}
