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

using System.Diagnostics;
using CognitiveGraph.Builder;
using CognitiveGraph.Schema;
using CogGraph = CognitiveGraph.CognitiveGraph;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #58 measurement 5: memory footprint (O(n) verification) and the
/// mmap-vs-resident working-set comparison. Not a BenchmarkDotNet suite —
/// run it in a fresh process per data point:
///
///   dotnet run -c Release -- --memory --mode graph  --size 1000
///   dotnet run -c Release -- --memory --mode naive   --size 1000
///   dotnet run -c Release -- --memory --mode mmap    --size 1000000
///   dotnet run -c Release -- --memory --mode resident --size 1000000
///
/// Each run prints one Markdown table row. Working set is measured after a
/// full collection with pending finalizers, as a delta against a pre-build
/// baseline snapshot in the same process.
/// </summary>
public static class MemoryFootprintRunner
{
    public static void Run(string[] args)
    {
        string mode = GetArg(args, "--mode") ?? "graph";
        int size = int.Parse(GetArg(args, "--size") ?? "10000");

        long baselineWs = SnapshotWorkingSet();
        long baselineHeap = GC.GetTotalMemory(forceFullCollection: true);
        long baselineAllocated = GC.GetTotalAllocatedBytes(precise: true);

        switch (mode)
        {
            case "graph":
                RunGraph(size, baselineWs, baselineHeap, baselineAllocated);
                break;
            case "naive":
                RunNaive(size, baselineWs, baselineHeap, baselineAllocated);
                break;
            case "mmap":
                RunMmap(size, baselineWs, resident: false);
                break;
            case "resident":
                RunMmap(size, baselineWs, resident: true);
                break;
            default:
                Console.Error.WriteLine($"Unknown mode: {mode}");
                Environment.Exit(2);
                break;
        }
    }

    private static void RunGraph(int size, long baselineWs, long baselineHeap, long baselineAllocated)
    {
        using var graph = GraphFactory.BuildCognitiveGraph(size, size * 128, out var offsets);
        var stats = graph.GetStatistics();
        long checksum = GraphFactory.Traverse(graph, offsets);
        long ws = SnapshotWorkingSet() - baselineWs;
        long heapDelta = GC.GetTotalMemory(forceFullCollection: true) - baselineHeap;
        long allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - baselineAllocated;

        Console.WriteLine($"| graph | {size} | {stats.BufferSize} | {(double)stats.BufferSize / size:F1} | {ToMiB(heapDelta)} | {ToMiB(allocatedDelta)} | {ToMiB(ws)} | {checksum % 7} |");
    }

    private static void RunNaive(int size, long baselineWs, long baselineHeap, long baselineAllocated)
    {
        var root = GraphFactory.BuildNaiveTree(size, out var nodes);
        long checksum = 0;
        foreach (var node in nodes)
            checksum += node.SymbolId + node.SourceStart;
        GC.KeepAlive(root);
        long ws = SnapshotWorkingSet() - baselineWs;
        long heapDelta = GC.GetTotalMemory(forceFullCollection: true) - baselineHeap;
        long allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - baselineAllocated;

        Console.WriteLine($"| naive | {size} | n/a | n/a | {ToMiB(heapDelta)} | {ToMiB(allocatedDelta)} | {ToMiB(ws)} | {checksum % 7} |");
    }

    private static void RunMmap(int size, long baselineWs, bool resident)
    {
        // Build the graph file (outside the measurement: reset the baseline
        // after the file exists).
        string file = Path.Combine(Path.GetTempPath(), $"cognitivegraph-mem-{size}.cg");
        var options = new GraphBuilderOptions { Schema = SchemaVersion.V1, InitialCapacity = size * 128 };
        using (var builder = new CognitiveGraphBuilder(options))
        {
            uint root = GraphFactory.BuildIntoBuilder(builder, size, out _);
            using var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None);
            builder.Build(stream, root, GraphFactory.MakeSourceText(size));
        }

        // The factory tree is deterministic: the file and a fresh in-memory
        // rebuild are identical in layout, so the symbol offsets can be read
        // from an in-memory rebuild and used against the mapped view / copy.
        // The probe runs BEFORE the baseline snapshot so its memory does not
        // pollute the measurement.
        var offsets = new uint[size];
        using (var probe = GraphFactory.BuildCognitiveGraph(size, size * 128, out var probeOffsets))
        {
            Array.Copy(probeOffsets, offsets, size);
        }

        baselineWs = SnapshotWorkingSet();
        long baselineHeap = GC.GetTotalMemory(forceFullCollection: true);
        long baselineAllocated = GC.GetTotalAllocatedBytes(precise: true);

        long checksum;
        long ws;
        long heapDelta;
        long allocatedDelta;

        if (resident)
        {
            byte[] bytes = File.ReadAllBytes(file);
            using var graph = CogGraph.FromBytes(bytes);
            checksum = GraphFactory.Traverse(graph, offsets);
            heapDelta = GC.GetTotalMemory(forceFullCollection: true) - baselineHeap;
            allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - baselineAllocated;
            ws = SnapshotWorkingSet() - baselineWs;
        }
        else
        {
            using var graph = new CogGraph(file);
            checksum = GraphFactory.Traverse(graph, offsets);
            heapDelta = GC.GetTotalMemory(forceFullCollection: true) - baselineHeap;
            allocatedDelta = GC.GetTotalAllocatedBytes(precise: true) - baselineAllocated;
            ws = SnapshotWorkingSet() - baselineWs;
        }

        long fileBytes = new FileInfo(file).Length;
        Console.WriteLine($"| {(resident ? "resident" : "mmap")} | {size} | {fileBytes} | {(double)fileBytes / size:F1} | {ToMiB(heapDelta)} | {ToMiB(allocatedDelta)} | {ToMiB(ws)} | {checksum % 7} |");
        try { File.Delete(file); } catch { /* best effort */ }
    }

    private static long SnapshotWorkingSet()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        GC.WaitForFullGCApproach(); // keep the runtime settled before sampling
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.WorkingSet64;
    }

    private static string ToMiB(long bytes) => $"{bytes / (1024.0 * 1024.0):F1} MiB";

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
