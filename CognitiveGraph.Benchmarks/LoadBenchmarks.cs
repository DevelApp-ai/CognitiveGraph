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

using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using CogGraph = CognitiveGraph.CognitiveGraph;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #58 measurement 6: memory-mapped load vs resident copy.
///
/// GlobalSetup builds a 100,000-node graph file once. Each operation then
/// loads it either through the mmap constructor (zero-copy: spans served
/// from the mapped view) or as a resident copy (File.ReadAllBytes +
/// FromBytes). The traversal step forces the pages in for both, so the
/// comparison is honest: it includes touching every node.
/// </summary>
[MemoryDiagnoser]
public class LoadBenchmarks
{
    private const int NodeCount = 100_000;
    private string _graphFile = null!;
    private uint[] _offsets = null!;

    [GlobalSetup(Targets = new[]
    {
        nameof(Load_MemoryMapped_Traverse),
        nameof(Load_MemoryMapped_Open),
        nameof(Load_ResidentCopy_Traverse)
    })]
    public void SetupFile()
    {
        _graphFile = Path.Combine(Path.GetTempPath(), $"cognitivegraph-bench-{NodeCount}.cg");
        var options = new GraphBuilderOptions
        {
            Schema = SchemaVersion.V1,
            InitialCapacity = NodeCount * 128
        };
        using var builder = new CognitiveGraphBuilder(options);
        uint root = GraphFactory.BuildIntoBuilder(builder, NodeCount, out _offsets);
        using var stream = new FileStream(_graphFile, FileMode.Create, FileAccess.Write, FileShare.None);
        builder.Build(stream, root, GraphFactory.MakeSourceText(NodeCount));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        try { File.Delete(_graphFile); } catch { /* best effort */ }
    }

    [Benchmark]
    public long Load_MemoryMapped_Traverse()
    {
        using var graph = new CogGraph(_graphFile);
        return GraphFactory.Traverse(graph, _offsets);
    }

    [Benchmark]
    public long Load_MemoryMapped_Open()
    {
        // Open only (no traversal): pages are faulted lazily, so this is the
        // mmap load time without page-in cost.
        using var graph = new CogGraph(_graphFile);
        return graph.GetStatistics().NodeCount;
    }

    [Benchmark]
    public long Load_ResidentCopy_Traverse()
    {
        byte[] bytes = File.ReadAllBytes(_graphFile);
        using var graph = CogGraph.FromBytes(bytes);
        return GraphFactory.Traverse(graph, _offsets);
    }
}
