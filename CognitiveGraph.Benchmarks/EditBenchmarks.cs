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
using CogGraph = CognitiveGraph.CognitiveGraph;
using CognitiveGraph.Schema;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #58 measurement 3: the editor workload from the article — a single
/// node rewrite and a single property update per edit, vs the immutable-tree
/// path rebuild and the in-place mutable edit.
///
/// CognitiveGraphEditor.Build() rebuilds the graph buffer through a new
/// builder (copy-on-write at the buffer level), so the single-edit cost is
/// expected to scale with graph size; the persistent tree rebuilds only the
/// root path (O(depth)). The benchmarks quantify exactly this trade-off —
/// they do not assume the article's claim in either direction.
/// </summary>
[MemoryDiagnoser]
public class EditBenchmarks
{
    [Params(1_000, 10_000, 100_000)]
    public int NodeCount;

    private CogGraph _graph = null!;
    private uint[] _offsets = null!;
    private uint _targetOffset;
    private PersistentNode _persistentRoot = null!;
    private PersistentNode[] _persistentNodes = null!;
    private NaiveObjectNode[] _naiveNodes = null!;
    private int _targetIndex;

    [GlobalSetup]
    public void Setup()
    {
        _graph = GraphFactory.BuildCognitiveGraph(NodeCount, NodeCount * 128, out _offsets);
        _targetIndex = GraphFactory.EditTargetIndex(NodeCount);
        _targetOffset = _offsets[_targetIndex];
        _persistentRoot = GraphFactory.BuildPersistentTree(NodeCount, out _persistentNodes);
        GraphFactory.BuildNaiveTree(NodeCount, out _naiveNodes);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graph.Dispose();
    }

    [Benchmark]
    public long Graph_ReplaceSingleNode()
    {
        using var editor = new CognitiveGraphEditor(_graph);
        editor.ReplaceNode(_targetOffset,
            newSymbolId: 999, newNodeType: 200,
            newSourceStart: 0, newSourceLength: 5);
        using var result = editor.Build();
        return result.GetStatistics().NodeCount;
    }

    [Benchmark]
    public long Graph_UpdateProperty()
    {
        using var editor = new CognitiveGraphEditor(_graph);
        editor.UpdateProperty(_targetOffset, GraphFactory.LineKey, PropertyValueType.Int32, 4242);
        using var result = editor.Build();
        return result.GetStatistics().NodeCount;
    }

    [Benchmark]
    public long PersistentTree_PathRebuild()
    {
        var target = _persistentNodes[_targetIndex];
        var replacement = new PersistentNode(999, 200, target.SourceStart, target.SourceLength,
            target.Children, target.Properties);
        var newRoot = _persistentRoot.ReplaceAt(_targetIndex, replacement);
        GC.KeepAlive(newRoot);
        return newRoot.SymbolId;
    }

    [Benchmark]
    public long NaiveObject_InPlaceEdit()
    {
        // Lower bound: mutate the shared object tree in place (the aliasing-
        // hazard edit the article argues against, included for scale).
        var target = _naiveNodes[_targetIndex];
        target.SymbolId = 999;
        return target.SymbolId;
    }
}
