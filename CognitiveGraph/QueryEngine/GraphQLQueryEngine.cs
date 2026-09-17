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
using System.Text.Json;
using System.Threading.Tasks;
using GraphQL;
using GraphQL.SystemTextJson;

namespace CognitiveGraph.QueryEngine;

/// <summary>
/// GraphQL query engine for CognitiveGraph.
/// Executes real GraphQL queries (parsed and validated by the GraphQL library)
/// against a schema backed by the graph's nodes, packed nodes, CPG edges,
/// properties and spatial index.
/// </summary>
/// <remarks>
/// The schema is created lazily on first execution and reused afterwards;
/// the underlying graph buffer is immutable after load.
/// V1 graphs are fully supported; V2 graphs fail fast with a clear error
/// (V2 accessor support is tracked separately).
/// </remarks>
public sealed class GraphQLQueryEngine
{
    private readonly CognitiveGraph _graph;
    private readonly Lazy<CognitiveGraphSchema> _schema;
    private readonly DocumentExecuter _executer = new();
    private readonly GraphQLSerializer _serializer = new();

    public GraphQLQueryEngine(CognitiveGraph graph)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _schema = new Lazy<CognitiveGraphSchema>(() => new CognitiveGraphSchema(_graph));
    }

    /// <summary>
    /// The GraphQL schema over the graph. Built on first access.
    /// </summary>
    public CognitiveGraphSchema Schema => _schema.Value;

    /// <summary>
    /// Executes a GraphQL query and returns the full execution result,
    /// including any parse, validation or execution errors.
    /// </summary>
    /// <example>
    /// <code>
    /// { nodes(symbolId: 1) { offset symbolId properties { key value } } }
    /// </code>
    /// </example>
    public Task<ExecutionResult> ExecuteAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));

        return _executer.ExecuteAsync(new ExecutionOptions
        {
            Schema = _schema.Value,
            Query = query,
        });
    }

    /// <summary>
    /// Executes a GraphQL query and returns the offsets of all nodes the query
    /// returned (every `offset` field in the response data).
    /// Throws <see cref="InvalidOperationException"/> if the query fails to
    /// parse, validate or execute.
    /// </summary>
    public async Task<List<uint>> ExecuteQueryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<uint>();

        var result = await ExecuteAsync(query);

        if (result.Errors is { Count: > 0 })
            throw new InvalidOperationException(
                $"GraphQL query failed: {result.Errors[0].Message}");

        if (result.Data is null)
            return new List<uint>();

        // Walk the serialized response and collect every node `offset` the caller
        // asked for. This keeps the historical List<uint> contract while the query
        // itself is fully user-defined.
        var json = _serializer.Serialize(result);
        using var document = JsonDocument.Parse(json);

        var offsets = new List<uint>();
        CollectOffsets(document.RootElement, offsets);
        return offsets;
    }

    private static void CollectOffsets(JsonElement element, List<uint> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "offset" && property.Value.ValueKind == JsonValueKind.Number)
                    {
                        var value = property.Value.GetInt64();
                        if (value >= 0 && value <= uint.MaxValue)
                            result.Add((uint)value);
                    }
                    else
                    {
                        CollectOffsets(property.Value, result);
                    }
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectOffsets(item, result);
                break;
        }
    }
}
