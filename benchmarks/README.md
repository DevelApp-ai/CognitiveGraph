# CognitiveGraph Benchmarks

The BenchmarkDotNet suite backing the measurement backlog
([DevelApp-ai/CognitiveGraph#58](https://github.com/DevelApp-ai/CognitiveGraph/issues/58),
cross-repo index:
[Research-article-builder#27](https://github.com/DevelApp-ai/Research-article-builder/issues/27)).
It converts the article's design targets (graph creation < 1 ms for 1K nodes,
property access ~10 ns, O(n) memory, minimal GC pressure, mmap load) into
measured numbers, and provides the naive AST comparison the
zero-copy-ast-manipulation article promises.

## What is measured

| Suite | Issue item | What it answers |
|---|---|---|
| `GraphCreationBenchmarks` | 1 | Graph creation for 1K/10K/100K nodes, cold (default 64 KB buffer, doubling growth) and pre-sized, vs naive object AST and persistent immutable AST |
| `PropertyAccessBenchmarks` | 2 | Span-based accessor cost vs object-node dictionary/field access (10K nodes) |
| `EditBenchmarks` | 3 | Single-node rewrite and property update (the editor workload) vs immutable-tree path rebuild vs in-place mutation |
| `SustainedEditBenchmarks` | 4 | Bytes allocated per edit + Gen0/1/2 collections over 100-edit sustained loops |
| `MemoryFootprintRunner` | 5 | Working set / bytes per node across 1K-1M nodes (O(n) check), mmap vs resident copy |
| `LoadBenchmarks` | 6 | mmap load vs resident copy (100K-node file), incl. lazy open-only load |
| `SchemaCrossoverRunner` | #70 | V1 (Compact) vs V2 (Universal) schema selection: build time, bytes/node, traversal speed, in-memory vs file-backed, semantic checksum (issue-70 measurement docs live in `docs/measurements/issue-70/`) |

## Running

```bash
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --job short
# or one suite at a time:
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --job short --filter '*EditBenchmarks*'
```

`--job short` is used for CI/quick runs (3 warmup + 3 iterations); for
publication-grade medians use the default job or `--job long`. Results land
in `BenchmarkDotNet.Artifacts/` (markdown + CSV). The MemoryDiagnoser
columns: `Allocated` = bytes per operation; `Gen0/1/2` = collections per
1000 operations.

Memory footprint runs one data point per fresh process (so each row is
independent) and prints one Markdown row per invocation. Columns:
live-heap delta (`GC.GetTotalMemory(true)`, deterministic — the O(n)
check), allocated delta (`GC.GetTotalAllocatedBytes`, cumulative garbage
included), working-set delta (approximate; the runtime may trim
segments, so small/negative values are possible):

```bash
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --memory --mode graph --size 1000
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --memory --mode naive --size 1000
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --memory --mode mmap --size 1000000
dotnet run -c Release --project CognitiveGraph.Benchmarks -- --memory --mode resident --size 1000000
```

The schema-crossover runner (issue #70) also runs one scale per fresh
process, accumulating rows into `<outdir>/results.json` keyed by
(Mode, Nodes):

```bash
dotnet run -c Release --project CognitiveGraph.Benchmarks -- \
  --schema-crossover --scale 1000000 --reps 3 --outdir docs/measurements/issue-70
```

## Protocol notes (interpretation of the issue's terms)

- **Cold vs warm creation.** "Cold" = default builder options (64 KB initial
  capacity, buffer grows by doubling while building) — what a parser sees
  on a fresh file. "PreSized" = initial capacity set to the final buffer
  size, isolating per-node write cost. JIT warm-up is handled by
  BenchmarkDotNet's warmup iterations in both cases.
- **Per-node access cost.** `PropertyAccessBenchmarks` walks a 10K-node
  graph per operation; the per-access cost is the reported median divided
  by the node count. Reported tables should state this.
- **Sustained edits.** One operation = 100 sequential edits (each edit ->
  a new graph version). Gen0/1/2 columns are therefore per 100,000 edits.
- **Mmap traversal.** Both mmap and resident loads fully traverse the
  graph (forcing page-in), so the comparison includes touching every node;
  `Load_MemoryMapped_Open` isolates the open-without-traversal cost.
- **Tree shape.** Complete binary tree in level-order indexing; every node
  is a symbol node with two properties; internal nodes carry a packed node
  linking children (see `GraphFactory`). All three representations build the
  identical logical tree.
- **Baselines.** `NaiveObjectNode` = mutable object AST (one heap object +
  child list + property dictionary per node). `PersistentNode` = immutable
  AST with structural sharing (edit rebuilds root path only). The naive
  in-place mutation is included as a lower bound only; it is the aliasing-
  hazard edit the article argues against.

## Committed results

See `results/` — one folder per environment, containing the BenchmarkDotNet
markdown export, the memory-footprint table, and the environment record
(CPU, OS, .NET version, invocation counts). Per the measurement-backlog
ground rules, an article number may only be replaced by a measurement that
is committed here with its environment recorded.
