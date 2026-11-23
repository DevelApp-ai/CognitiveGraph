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
using System.Text;
using System.Text.Json;
using CognitiveGraph.Accessors;
using CognitiveGraph.Builder;
using CognitiveGraph.Buffer;
using CognitiveGraph.Schema;
using CogGraph = CognitiveGraph.CognitiveGraph;

namespace CognitiveGraph.Benchmarks;

/// <summary>
/// Issue #70 measurement harness: V1 (Compact) vs V2 (Universal) schema
/// selection. Not a BenchmarkDotNet suite — a deterministic console runner:
///
///   dotnet run -c Release --project CognitiveGraph.Benchmarks -- \
///     --schema-crossover --scale 1000000 --reps 3 --outdir docs/measurements/issue-70
///
/// One invocation per scale (fresh process), mirroring MemoryFootprintRunner:
/// builder growth copies and per-rep GC garbage peak at several times the
/// final buffer size, so accumulating large scales in one process can exceed
/// container memory. Rows accumulate in the outdir's results.json keyed by
/// (Mode, Nodes), so the last invocation finishes the dataset.
///
/// The "version-selection thresholds" under validation are the shipped
/// defaults deciding when a graph uses the V1 (Compact) or V2 (Universal)
/// layout. The harness measures, per scale (symbol-node count), the same
/// deterministic tree (GraphFactory) built through the public builder in
/// both schemas, in-memory and file-backed:
///
/// - v1-mem: GraphBuilderOptions default path (V1 in-memory buffer) — the
///   per-file path parsers use today.
/// - v1-mmap: V1 persisted to a file, loaded read-only through the
///   memory-mapped constructor.
/// - v2-mem: V2 in-memory layout (build side only; the full V2 read
///   accessors require the file-backed buffer, which is itself a
///   selection fact).
/// - v2-mmap: V2 persisted to a file, loaded through the memory-mapped
///   path with full SymbolNode64 access — the whole-system/large-graph
///   path.
///
/// Per row it records build time (median over reps, fresh builder per rep),
/// buffer/file bytes, traversal time (median over reps, warm accessors),
/// a content checksum (identical across schemas when the layouts are
/// faithful), and build-phase managed allocations. Rows print as Markdown
/// and the full result set is written to results.json when --outdir is set.
/// </summary>
public static class SchemaCrossoverRunner
{
    public sealed record Row(
        string Mode,
        int Nodes,
        int Reps,
        double BuildMs,
        double? TraverseMs,
        long Bytes,
        double BuildAllocMB,
        long Checksum,
        string Notes);

    public static void Run(string[] args)
    {
        // --scale N runs a single scale in this process. Memory limits make the
        // larger scales unsafe to accumulate in one process (the builder's
        // growth copies plus the GC garbage of repeated builds peak at
        // several times the final buffer size), so the documented protocol is
        // one invocation per scale, exactly like MemoryFootprintRunner. Rows
        // accumulate in the outdir's results.json keyed by (Mode, Nodes).
        string? singleScale = GetArg(args, "--scale");
        int[] scales = singleScale is not null
            ? new[] { int.Parse(singleScale) }
            : ParseScales(GetArg(args, "--scales") ?? "1000,10000,100000");
        int reps = int.Parse(GetArg(args, "--reps") ?? "3");
        string? outdir = GetArg(args, "--outdir");

        var rows = new List<Row>();
        foreach (int scale in scales)
        {
            rows.AddRange(MeasureScale(scale, reps));
        }

        PrintTable(rows);
        if (outdir is not null)
        {
            rows = MergeWithExisting(outdir, rows);
            Directory.CreateDirectory(outdir);
            var json = JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(outdir, "results.json"), json + "\n");
            Console.WriteLine($"results.json written to {outdir}");
        }

        // Content check: identical trees must produce identical checksums
        // in every readable mode, or the schema comparison is invalid.
        foreach (var group in rows.GroupBy(r => r.Nodes))
        {
            var checksums = group.Select(r => r.Checksum).Where(c => c != 0).Distinct().ToList();
            if (checksums.Count > 1)
            {
                Console.WriteLine($"WARNING: checksum mismatch at {group.Key} nodes: {string.Join(", ", checksums)}");
            }
        }
    }

    private static IEnumerable<Row> MeasureScale(int nodes, int reps)
    {
        ReadOnlyMemory<byte> source = Encoding.UTF8.GetBytes(GraphFactory.MakeSourceText(nodes));

        // v1-mem — the default per-file path.
        {
            double buildMs = MeasureBuilds(SchemaVersion.V1, nodes, source, reps, out double buildAlloc, out long bytes);
            using var buffer = BuildOnce(SchemaVersion.V1, nodes, source, out uint[] offsets, out _);
            using var graph = new CogGraph(buffer);
            TraverseV1(graph, offsets); // warm
            double traverseMs = Median(TraverseTimed(() => TraverseV1(graph, offsets), reps));
            long checksum = TraverseV1(graph, offsets);
            yield return new Row("v1-mem", nodes, reps, buildMs, traverseMs, bytes, buildAlloc, checksum,
                "in-memory V1 buffer (default GraphBuilderOptions); build = tree construction + final buffer; GetNodeAt traversal");
        }

        // v2-mem — build side only: the full V2 read accessors require the
        // file-backed UniversalGraphBuffer (selection-relevant fact).
        {
            double buildMs = MeasureBuilds(SchemaVersion.V2, nodes, source, reps, out double buildAlloc, out long bytes);
            using var buffer = BuildOnce(SchemaVersion.V2, nodes, source, out _, out _);
            using var graph = new CogGraph(buffer);
            string note = "in-memory V2 layout; full V2 accessors need the file-backed buffer";
            try
            {
                _ = graph.GetStatistics();
                note += "; GetStatistics() works";
            }
            catch (Exception ex)
            {
                note += $"; GetStatistics() threw {ex.GetType().Name}";
            }
            yield return new Row("v2-mem", nodes, reps, buildMs, null, bytes, buildAlloc, 0, note);
        }

        // v1-mmap — persisted V1, memory-mapped read-only load.
        {
            string file = Path.Combine(Path.GetTempPath(), $"cognitivegraph-crossover-v1-{nodes}.cg");
            double buildMs = MeasureFileBuilds(SchemaVersion.V1, nodes, source, file, reps, out double buildAlloc);
            using var graph = new CogGraph(file);
            uint[] offsets = OffsetsFromFreshBuild(SchemaVersion.V1, nodes, source, out _);
            TraverseV1(graph, offsets); // warm the mapped view
            double traverseMs = Median(TraverseTimed(() => TraverseV1(graph, offsets), reps));
            long checksum = TraverseV1(graph, offsets);
            long bytes = new FileInfo(file).Length;
            File.Delete(file);
            yield return new Row("v1-mmap", nodes, reps, buildMs, traverseMs, bytes, buildAlloc, checksum,
                "V1 persisted, loaded via memory-mapped constructor; build = tree construction + file I/O; GetNodeAt traversal");
        }

        // v2-mmap — persisted V2, memory-mapped load with SymbolNode64 access.
        {
            string file = Path.Combine(Path.GetTempPath(), $"cognitivegraph-crossover-v2-{nodes}.cg");
            double buildMs = MeasureFileBuilds(SchemaVersion.V2, nodes, source, file, reps, out double buildAlloc);
            using var graph = new CogGraph(file);
            ulong[] offsets = OffsetsV2FromFreshBuild(nodes, source, out _);
            TraverseV2(graph, offsets); // warm
            double traverseMs = Median(TraverseTimed(() => TraverseV2(graph, offsets), reps));
            long checksum = TraverseV2(graph, offsets);
            long bytes = new FileInfo(file).Length;
            File.Delete(file);
            yield return new Row("v2-mmap", nodes, reps, buildMs, traverseMs, bytes, buildAlloc, checksum,
                "V2 persisted, loaded via memory-mapped constructor; build = tree construction + file I/O; GetNodeAtV2 traversal");
        }
    }

    /// <summary>
    /// Loads an existing results.json (if any), drops rows re-measured by
    /// this invocation (same Mode + Nodes), appends the fresh rows and sorts
    /// by scale then mode, so per-scale invocations accumulate into one file.
    /// </summary>
    private static List<Row> MergeWithExisting(string outdir, List<Row> fresh)
    {
        string file = Path.Combine(outdir, "results.json");
        if (!File.Exists(file))
            return fresh.OrderBy(r => r.Nodes).ThenBy(r => r.Mode).ToList();
        try
        {
            var existing = JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(file)) ?? new List<Row>();
            var freshKeys = fresh.Select(r => (r.Mode, r.Nodes)).ToHashSet();
            return existing
                .Where(r => !freshKeys.Contains((r.Mode, r.Nodes)))
                .Concat(fresh)
                .OrderBy(r => r.Nodes)
                .ThenBy(r => r.Mode)
                .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"WARNING: could not merge with existing results.json ({ex.GetType().Name}); overwriting");
            return fresh.OrderBy(r => r.Nodes).ThenBy(r => r.Mode).ToList();
        }
    }

    // ---------- build helpers ----------

    private static CognitiveGraphBuffer BuildOnce(SchemaVersion schema, int nodes, ReadOnlyMemory<byte> source,
        out uint[] offsets, out long bytes)
    {
        var options = new GraphBuilderOptions { Schema = schema, InitialCapacity = Math.Max(64 * 1024, nodes * 128) };
        using var builder = new CognitiveGraphBuilder(options);
        uint root = GraphFactory.BuildIntoBuilder(builder, nodes, out offsets);
        var buffer = builder.Build(root, source);
        bytes = buffer.Length;
        return buffer;
    }

    /// <summary>
    /// Median build time / allocations over reps fresh builders; content is
    /// deterministic so only the final rep's buffer size (returned) matters.
    /// </summary>
    private static double MeasureBuilds(SchemaVersion schema, int nodes, ReadOnlyMemory<byte> source, int reps,
        out double buildAllocMB, out long bytes)
    {
        var times = new List<double>();
        var allocs = new List<double>();
        bytes = 0;
        for (int r = 0; r < reps; r++)
        {
            Cleanup();
            long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var sw = Stopwatch.StartNew();
            using var buffer = BuildOnce(schema, nodes, source, out _, out bytes);
            sw.Stop();
            allocs.Add((GC.GetTotalAllocatedBytes(precise: true) - allocBefore) / (1024.0 * 1024.0));
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Cleanup();
        buildAllocMB = Median(allocs);
        return Median(times);
    }

    /// <summary>
    /// Median time for the full build (tree construction + straight-to-file
    /// serialization, including file I/O). The final rep's file (same
    /// deterministic content each rep) is left on disk for loading.
    /// </summary>
    private static double MeasureFileBuilds(SchemaVersion schema, int nodes, ReadOnlyMemory<byte> source,
        string file, int reps, out double buildAllocMB)
    {
        var times = new List<double>();
        var allocs = new List<double>();
        for (int r = 0; r < reps; r++)
        {
            Cleanup();
            long allocBefore = GC.GetTotalAllocatedBytes(precise: true);
            var options = new GraphBuilderOptions { Schema = schema, InitialCapacity = Math.Max(64 * 1024, nodes * 128) };
            var sw = Stopwatch.StartNew();
            using var builder = new CognitiveGraphBuilder(options);
            uint root = GraphFactory.BuildIntoBuilder(builder, nodes, out _);
            using (var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                builder.Build(stream, root, source);
            }
            sw.Stop();
            allocs.Add((GC.GetTotalAllocatedBytes(precise: true) - allocBefore) / (1024.0 * 1024.0));
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        Cleanup();
        buildAllocMB = Median(allocs);
        return Median(times);
    }

    private static uint[] OffsetsFromFreshBuild(SchemaVersion schema, int nodes, ReadOnlyMemory<byte> source, out long bytes)
    {
        // The factory tree is deterministic: a fresh in-memory rebuild has the
        // same layout as the persisted file, so its symbol offsets are valid
        // against the loaded view (same trick as MemoryFootprintRunner).
        using var buffer = BuildOnce(schema, nodes, source, out uint[] offsets, out bytes);
        return offsets;
    }

    private static ulong[] OffsetsV2FromFreshBuild(int nodes, ReadOnlyMemory<byte> source, out long bytes)
    {
        // The V2 builder writes ulong offsets; the public WriteSymbolNode API
        // returns uint. Under 2 GiB the cast is lossless, asserted per offset.
        using var buffer = BuildOnce(SchemaVersion.V2, nodes, source, out uint[] offsets32, out bytes);
        bytes = buffer.Length;
        var offsets = new ulong[offsets32.Length];
        for (int i = 0; i < offsets32.Length; i++)
        {
            offsets[i] = offsets32[i];
            if ((ulong)(uint)offsets[i] != offsets[i])
                throw new InvalidOperationException(
                    "V2 offset exceeded uint range; harness cannot run this scale in-process");
        }
        return offsets;
    }

    // ---------- traversal helpers ----------

    private static long TraverseV1(CogGraph graph, uint[] offsets)
    {
        long sum = 0;
        foreach (uint offset in offsets)
        {
            SymbolNode node = graph.GetNodeAt(offset);
            sum += node.SymbolID + node.SourceStart;
            foreach (var property in node.GetProperties())
            {
                var value = property.GetValue();
                if (value.Type == PropertyValueType.Int32)
                    sum += value.AsInt32();
            }
        }
        return sum;
    }

    private static long TraverseV2(CogGraph graph, ulong[] offsets)
    {
        long sum = 0;
        foreach (ulong offset in offsets)
        {
            SymbolNode64 node = graph.GetNodeAtV2(offset);
            sum += node.SymbolID + node.SourceStart;
            foreach (var property in node.GetProperties())
            {
                var value = property.GetValue();
                if (value.Type == PropertyValueType.Int32)
                    sum += value.AsInt32();
            }
        }
        return sum;
    }

    private static List<double> TraverseTimed(Action traverse, int reps)
    {
        var times = new List<double>();
        for (int r = 0; r < reps; r++)
        {
            var sw = Stopwatch.StartNew();
            traverse();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        return times;
    }

    // ---------- misc ----------

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    private static void Cleanup()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void PrintTable(IEnumerable<Row> rows)
    {
        Console.WriteLine();
        Console.WriteLine("| Mode | Nodes | Build ms (med) | Bytes | B/node | Traverse ms (med) | Build alloc MB | Checksum |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var r in rows)
        {
            Console.WriteLine($"| {r.Mode} | {r.Nodes} | {r.BuildMs:F1} | {r.Bytes} | {(double)r.Bytes / r.Nodes:F1} | " +
                $"{(r.TraverseMs?.ToString("F1") ?? "n/a")} | {r.BuildAllocMB:F1} | {r.Checksum} |");
        }
    }

    private static int[] ParseScales(string csv)
    {
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var scales = new List<int>();
        foreach (var p in parts)
        {
            if (int.TryParse(p, out int v) && v > 0)
                scales.Add(v);
        }
        return scales.Count > 0 ? scales.ToArray() : new[] { 1000, 10000, 100000, 1000000 };
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
