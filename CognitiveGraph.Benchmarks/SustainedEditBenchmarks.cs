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
/// Issue #58 measurement 4: GC pressure under a sustained edit loop — the
/// editor reparsing-per-keystroke workload from the article.
///
/// One operation = EditsPerLoop sequential single-node rewrites, each
/// producing a new version (edit -> build on CognitiveGraph, path rebuild
/// on the persistent tree, whole-tree rebuild on the naive AST). Kept at
/// 10,000 nodes so one operation stays in the seconds range.
///
/// BenchmarkDotNet's MemoryDiagnoser reports Gen0/Gen1/Gen2 collection
/// counts per 1000 operations — i.e. per 1000 x EditsPerLoop edits — plus
/// bytes allocated per operation.
/// </summary>
[MemoryDiagnoser]
public class SustainedEditBenchmarks
{
    private const int NodeCount = 10_000;
    public const int EditsPerLoop = 100;

    private CogGraph _graph = null!;
    private uint[] _offsets = null!;
    private PersistentNode _persistentRoot = null!;
    private PersistentNode[] _persistentNodes = null!;
    private NaiveObjectNode _naiveRoot = null!;

    [GlobalSetup]
    public void Setup()
    {
        _graph = GraphFactory.BuildCognitiveGraph(NodeCount, NodeCount * 128, out _offsets);
        _persistentRoot = GraphFactory.BuildPersistentTree(NodeCount, out _persistentNodes);
        _naiveRoot = GraphFactory.BuildNaiveTree(NodeCount, out _);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _graph.Dispose();
    }

    [Benchmark]
    public long Graph_SustainedEdits_100()
    {
        long checksum = 0;
        for (int i = 0; i < EditsPerLoop; i++)
        {
            uint target = _offsets[(i * 37) % NodeCount];
            using var editor = new CognitiveGraphEditor(_graph);
            editor.ReplaceNode(target,
                newSymbolId: (ushort)(900 + i % 100), newNodeType: 200,
                newSourceStart: 0, newSourceLength: 5);
            using var result = editor.Build();
            checksum += result.GetStatistics().NodeCount;
        }
        return checksum;
    }

    [Benchmark]
    public long Graph_SustainedPropertyUpdates_100()
    {
        long checksum = 0;
        for (int i = 0; i < EditsPerLoop; i++)
        {
            uint target = _offsets[(i * 41) % NodeCount];
            using var editor = new CognitiveGraphEditor(_graph);
            editor.UpdateProperty(target, GraphFactory.LineKey, PropertyValueType.Int32, i);
            using var result = editor.Build();
            checksum += result.GetStatistics().NodeCount;
        }
        return checksum;
    }

    [Benchmark]
    public long PersistentTree_SustainedPathRebuilds_100()
    {
        long checksum = 0;
        for (int i = 0; i < EditsPerLoop; i++)
        {
            int targetIndex = (i * 37) % NodeCount;
            var target = _persistentNodes[targetIndex];
            var replacement = new PersistentNode((ushort)(900 + i % 100), 200,
                target.SourceStart, target.SourceLength, target.Children, target.Properties);
            var newRoot = _persistentRoot.ReplaceAt(targetIndex, replacement);
            checksum += newRoot.SymbolId;
        }
        return checksum;
    }

    [Benchmark]
    public long NaiveTree_SustainedRebuilds_100()
    {
        // The conventional immutable-object pipeline without structural
        // sharing: every edit rebuilds the whole object tree.
        long checksum = 0;
        for (int i = 0; i < EditsPerLoop; i++)
        {
            GraphFactory.BuildNaiveTree(NodeCount, out var nodes);
            nodes[0].SymbolId = (ushort)(900 + i % 100);
            checksum += nodes[0].SymbolId;
        }
        GC.KeepAlive(_naiveRoot);
        return checksum;
    }
}
