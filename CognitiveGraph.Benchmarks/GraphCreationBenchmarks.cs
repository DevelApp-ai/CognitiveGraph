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

using BenchmarkDotNet.Attributes;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #58 measurement 1: graph creation for 1K / 10K / 100K symbol
/// nodes, cold and warm.
///
/// - "Cold" = default builder options (64 KB initial capacity): the buffer
///   grows by doubling during the build, which is what a parser sees on a
///   fresh file.
/// - "PreSized" = initial capacity set to the final buffer size, isolating
///   the per-node write cost from amortized buffer growth.
/// - Baselines: the same tree as a naive mutable object AST and as a
///   persistent immutable AST.
/// </summary>
[MemoryDiagnoser]
public class GraphCreationBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int NodeCount;

    // ~40 bytes of buffer per symbol node (node struct + property records +
    // packed nodes + string table), sized generously so "PreSized" never grows.
    private int PreSizedCapacity => NodeCount * 128;

    [Benchmark(Baseline = true)]
    public long CognitiveGraph_Cold()
    {
        using var graph = GraphFactory.BuildCognitiveGraph(NodeCount, 64 * 1024, out _);
        return graph.GetStatistics().BufferSize;
    }

    [Benchmark]
    public long CognitiveGraph_PreSized()
    {
        using var graph = GraphFactory.BuildCognitiveGraph(NodeCount, PreSizedCapacity, out _);
        return graph.GetStatistics().BufferSize;
    }

    [Benchmark]
    public long NaiveObjectTree_Create()
    {
        var root = GraphFactory.BuildNaiveTree(NodeCount, out var nodes);
        long sum = 0;
        for (int i = 0; i < nodes.Length; i++)
            sum += nodes[i].SymbolId + nodes[i].SourceStart;
        GC.KeepAlive(root);
        return sum;
    }

    [Benchmark]
    public long PersistentTree_Create()
    {
        var root = GraphFactory.BuildPersistentTree(NodeCount, out var nodes);
        long sum = 0;
        for (int i = 0; i < nodes.Length; i++)
            sum += nodes[i].SymbolId + nodes[i].SourceStart;
        GC.KeepAlive(root);
        return sum;
    }
}
