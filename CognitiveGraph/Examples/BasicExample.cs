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
using CognitiveGraph;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Examples;

/// <summary>
/// Simple example demonstrating how to create and use a Cognitive Graph
/// </summary>
public static class BasicExample
{
    public static void Run()
    {
        Console.WriteLine("Creating a simple Cognitive Graph...");

        // Create a graph for a simple expression: "hello + world"
        using var builder = new CognitiveGraphBuilder();

        // Create properties for the expression
        var properties = new List<(string key, PropertyValueType type, object value)>
        {
            ("NodeType", PropertyValueType.String, "BinaryExpression"),
            ("Operator", PropertyValueType.String, "+")
        };

        // Write the root expression node
        var rootNodeOffset = builder.WriteSymbolNode(
            symbolId: 1,      // Expression
            nodeType: 200,    // BinaryExpression
            sourceStart: 0,
            sourceLength: 13, // "hello + world"
            properties: properties
        );

        // Build the final graph
        var buffer = builder.Build(rootNodeOffset, "hello + world");

        // Use the graph
        using var graph = new CognitiveGraph(buffer);

        Console.WriteLine($"Source text: {graph.GetSourceText()}");
        Console.WriteLine($"Is fully parsed: {graph.IsFullyParsed}");

        var rootNode = graph.GetRootNode();
        Console.WriteLine($"Root node type: {rootNode.NodeType}");
        Console.WriteLine($"Root node symbol: {rootNode.SymbolID}");
        Console.WriteLine($"Source span: {rootNode.SourceStart}-{rootNode.SourceEnd}");

        // Access properties
        if (rootNode.TryGetProperty("NodeType", out var nodeType))
        {
            Console.WriteLine($"Node type property: {nodeType.AsString()}");
        }

        if (rootNode.TryGetProperty("Operator", out var operatorProp))
        {
            Console.WriteLine($"Operator: {operatorProp.AsString()}");
        }

        // Get statistics
        var stats = graph.GetStatistics();
        Console.WriteLine($"Graph statistics:");
        Console.WriteLine($"  Nodes: {stats.NodeCount}");
        Console.WriteLine($"  Edges: {stats.EdgeCount}");
        Console.WriteLine($"  Buffer size: {stats.BufferSize} bytes");
        Console.WriteLine($"  Density: {stats.NodesPerKb:F2} nodes/KB");

        Console.WriteLine("Example completed successfully!");
    }
}