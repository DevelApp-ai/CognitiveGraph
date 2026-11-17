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
using CognitiveGraph.Accessors;
using CognitiveGraph.Schema;
using CogGraph = CognitiveGraph.CognitiveGraph;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #58 measurement 2: property/node access via the span-based
/// accessors vs the object-node AST baseline.
///
/// Fixed at 10,000 nodes: access is per-node and linear, so the per-node
/// cost is the reported median divided by 10,000 (see benchmarks/README.md).
///
/// - TryGetProperty walks the property list comparing keys (the documented
///   lookup path; key reads materialize strings).
/// - SpanPropertyEnumeration walks the same properties through the
///   zero-allocation enumerator without key lookups.
/// - Naive baselines: dictionary lookup and field access on the object AST.
/// </summary>
[MemoryDiagnoser]
public class PropertyAccessBenchmarks
{
    private const int NodeCount = 10_000;

    private CogGraph _graph = null!;
    private uint[] _offsets = null!;
    private NaiveObjectNode[] _naiveNodes = null!;

    [GlobalSetup]
    public void Setup()
    {
        _graph = GraphFactory.BuildCognitiveGraph(NodeCount, NodeCount * 128, out _offsets);
        GraphFactory.BuildNaiveTree(NodeCount, out _naiveNodes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graph.Dispose();
    }

    [Benchmark]
    public long Graph_HeaderAccess()
    {
        long sum = 0;
        foreach (uint offset in _offsets)
        {
            SymbolNode node = _graph.GetNodeAt(offset);
            sum += node.SymbolID + node.SourceStart;
        }
        return sum;
    }

    [Benchmark]
    public long Graph_TryGetPropertyByKey()
    {
        long sum = 0;
        foreach (uint offset in _offsets)
        {
            SymbolNode node = _graph.GetNodeAt(offset);
            if (node.TryGetProperty(GraphFactory.LineKey, out PropertyValue value))
                sum += value.AsInt32();
        }
        return sum;
    }

    [Benchmark]
    public long Graph_SpanPropertyEnumeration()
    {
        long sum = 0;
        foreach (uint offset in _offsets)
        {
            SymbolNode node = _graph.GetNodeAt(offset);
            foreach (var property in node.GetProperties())
            {
                var value = property.GetValue();
                if (value.Type == PropertyValueType.Int32)
                    sum += value.AsInt32();
            }
        }
        return sum;
    }

    [Benchmark]
    public long Naive_FieldAccess()
    {
        long sum = 0;
        foreach (var node in _naiveNodes)
            sum += node.SymbolId + node.SourceStart;
        return sum;
    }

    [Benchmark]
    public long Naive_DictionaryPropertyLookup()
    {
        long sum = 0;
        foreach (var node in _naiveNodes)
            sum += (int)node.Properties[GraphFactory.LineKey]!;
        return sum;
    }
}
