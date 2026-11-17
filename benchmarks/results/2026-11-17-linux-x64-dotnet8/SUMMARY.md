# Benchmark results — 2026-11-17, Linux x64, .NET 8.0.34 (ShortRun)

Raw BenchmarkDotNet exports: the `CognitiveGraph.Benchmarks.*-report-github.md`
files in this folder. Medians below are the ShortRun medians (3 iterations);
see `environment.md` for hardware/software and how to reproduce.

## Design targets vs measured values (issue #58)

| Article claim (design target) | Measured (median) | Verdict |
|---|---|---|
| Graph creation < 1 ms for 1K nodes | **0.95 ms** cold (default 64 KB buffer); 0.91 ms pre-sized | **Met** at 1K nodes (but 11.2 ms for 10K, 83 ms for 100K — the target is per-1K-nodes) |
| Property access ~ 10 ns | Header access **1.6 ns**/node; span property read **~18.6 ns**/property (1 B alloc); keyed `TryGetProperty` **~184 ns** (string keys allocate) | Order-of-magnitude confirmed; span path measured 18.6 ns, keyed lookup is ~10x the target |
| O(n) memory | Live heap 0.1 / 1.0 / 10.3 / 103.0 MiB for 1K/10K/100K/1M nodes (104 B/node) | **Confirmed** linear |
| Minimal GC pressure (allocation-free span access) | Span enumeration allocates **1 B/op** (vs 72 B/node for keyed lookup); graph build churns ~590 B/node (collected garbage) | Span reads are allocation-free as claimed; **the edit path is not** (see below) |
| mmap load without resident copy | mmap load: **0 bytes** allocated, 0 live heap; resident copy: **+99.2 MiB** (= file size) at 1M nodes | **Confirmed** |

## Headline medians

### Graph creation (per tree)

| Method | 1K nodes | 10K nodes | 100K nodes |
|---|---:|---:|---:|
| CognitiveGraph cold (64 KB start, doubling) | 0.95 ms | 11.2 ms | 82.9 ms |
| CognitiveGraph pre-sized | 0.91 ms | 10.6 ms | 26.5 ms |
| Naive object AST | 0.073 ms | 0.93 ms | 15.9 ms |
| Persistent immutable AST | 0.067 ms | 0.82 ms | 16.1 ms |

Creation of the packed buffer is ~1 µs/node — ~10x slower than building
the equivalent object tree. The buffer's per-node write path (property
records, string table, interval tree, packed nodes) does more work than
`new` + two dictionary inserts.

### Property/node access (per operation over a 10K-node graph)

| Method | Median | Per node | Allocated |
|---|---:|---:|---:|
| Graph header access (SymbolID/SourceStart) | 16.1 µs | 1.6 ns | 0 B |
| Graph span property enumeration (20K props) | 371 µs | 18.6 ns/property | 1 B |
| Graph keyed TryGetProperty | 1,839 µs | 184 ns | 72 B/node |
| Naive field access | 7.2 µs | 0.7 ns | 0 B |
| Naive dictionary lookup | 138.9 µs | 13.9 ns | 0 B |

### Single edit (the editor workload)

| Method | 1K nodes | 10K nodes | 100K nodes |
|---|---:|---:|---:|
| CognitiveGraph replace-node (edit -> Build) | 2.0 ms | 24.0 ms | 293 ms |
| CognitiveGraph update-property (edit -> Build) | 2.1 ms | 24.3 ms | 314 ms |
| Persistent AST path rebuild (O(depth)) | 0.40 µs | 0.59 µs | 0.74 µs |
| Naive in-place mutation (lower bound) | ~0 ns | ~0 ns | ~0 ns |

**Finding:** `CognitiveGraphEditor.Build()` rebuilds the entire graph
through a fresh builder, so one single-node edit costs O(graph) time and
a full buffer copy (10.3 MB allocated per edit at 10K nodes; Gen0/1/2 all
churn). The persistent-tree baseline performs the same logical edit in
0.6 µs with 1.3 KB allocated. The edit path is currently **not**
proportional to the change — it is proportional to the file.

### Sustained edits (100 sequential edits, 10K-node graph)

| Method | Median | Allocated | Gen0 / Gen1 / Gen2 per 100k edits |
|---|---:|---:|---|
| Graph replace-node x100 | 969 ms | 1,001,903 KB (~1 GB) | 79,000 / 79,000 / 79,000 |
| Graph update-property x100 | 872 ms | 1,001,911 KB | 79,000 / 79,000 / 79,000 |
| Persistent AST path rebuilds x100 | 50.7 µs | 109 KB | 1.2 / – / – |
| Naive full-tree rebuild x100 | 86.1 ms | 347,659 KB | 4,200 / 2,000 / – |

The sustained editor workload allocates ~10 MB **per edit** and triggers
full Gen2 collections constantly — the opposite of the article's
"minimal GC pressure under edit workloads" claim at its current state.

### Load: mmap vs resident copy (100K-node, 10.4 MB file)

| Method | Median | Allocated |
|---|---:|---:|
| mmap load + full traversal | 14.0 ms | 1.96 KB |
| mmap load, open only (lazy) | 0.31 ms | 1.92 KB |
| resident copy (ReadAllBytes + FromBytes) + traversal | 11.1 ms | 10,158.86 KB |

mmap load is zero-allocation and lazy (0.3 ms to open), but traversal is
~25% slower than the resident copy (page faults against a cold file).
Both numbers belong in the article's trade-off discussion.

## What this means for the article (zero-copy-ast-manipulation §5)

Measured and can be upgraded from design target: creation < 1 ms @ 1K
(met); ~10 ns access order-of-magnitude (1.6-18.6 ns); O(n) memory
(confirmed, 104 B/node, ~3.4x less live memory than object AST); mmap
zero-copy load (confirmed).

Measured and **currently refuted**: edit-path incrementality and
edit-workload GC pressure. `CognitiveGraphEditor.Build()` is a full
rebuild; a single edit costs O(n) time + a full buffer copy. The article's
Section 4 claim ("cost of an edit proportional to the change rather than
the file") must stay labeled as a design target until the editor gains a
buffer-level incremental build (tracked follow-up), or be reworded to
describe the current copy-on-write semantics honestly.

Also verified: the library's test suite is **113 tests, all passing**
(`dotnet test -c Release`, .NET 8, Linux) — the article's "102 unit
tests" figure is stale.
