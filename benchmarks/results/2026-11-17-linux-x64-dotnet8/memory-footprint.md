# Memory footprint — 2026-11-17, Linux x64, .NET 8

Columns: **live heap delta** = `GC.GetTotalMemory(true)` after build minus
baseline (deterministic, the O(n) check); **allocated delta** =
`GC.GetTotalAllocatedBytes(precise)` cumulative allocation during the run
(garbage included); **working-set delta** = process working set after full
collection minus baseline (approximate: the runtime may trim segments, so
small/negative values are possible and this column is indicative only).

One fresh process per row (`--memory --mode <mode> --size <n>`).

| Mode | Nodes | Buffer/file bytes | Bytes/node | Live heap delta | Allocated delta | Working-set delta | Check |
|---|---:|---:|---:|---:|---:|---:|---:|
| graph  | 1000 | 104,046 | 104.0 | 0.1 MiB | 0.6 MiB | 4.6 MiB | 4 |
| graph  | 10000 | 1,040,046 | 104.0 | 1.0 MiB | 6.0 MiB | 11.8 MiB | 3 |
| graph  | 100000 | 10,400,046 | 104.0 | 10.3 MiB | 59.5 MiB | 63.0 MiB | 5 |
| graph  | 1000000 | 104,000,046 | 104.0 | 103.0 MiB | 588.6 MiB | 319.1 MiB | 1 |
| naive  | 1000 | n/a | n/a | 0.3 MiB | 0.4 MiB | 2.8 MiB | 3 |
| naive  | 10000 | n/a | n/a | 3.4 MiB | 3.4 MiB | 6.6 MiB | 4 |
| naive  | 100000 | n/a | n/a | 34.0 MiB | 34.0 MiB | 37.2 MiB | 2 |
| naive  | 1000000 | n/a | n/a | 339.5 MiB | 339.6 MiB | 344.3 MiB | 1 |
| mmap | 100000 | 10,400,046 | 104.0 | 0.0 MiB | 0.0 MiB | 4.6 MiB | 5 |
| resident | 100000 | 10,400,046 | 104.0 | 9.9 MiB | 9.9 MiB | -3.5 MiB | 5 |
| mmap | 1000000 | 104,000,046 | 104.0 | -0.0 MiB | 0.0 MiB | 40.7 MiB | 1 |
| resident | 1000000 | 104,000,046 | 104.0 | 99.2 MiB | 99.2 MiB | -35.5 MiB | 1 |

## Reading

- **O(n) memory confirmed.** Live heap for the CognitiveGraph grows
  0.1 -> 1.0 -> 10.3 -> 103.0 MiB across 1K -> 1M nodes — linear, at
  ~104 bytes per node (the buffer itself is exactly 104 B/node in this
  corpus).
- **Zero-copy graph vs naive object AST:** ~104 B/node live vs ~356 B/node
  for the naive tree (~3.4x less live memory at 1M nodes).
- **Cumulative allocation churn** building the graph is ~590 B/node —
  garbage that is collected, not live memory (mostly property lists and
  builder bookkeeping on the caller side; see also the allocation
  regression tests in `CognitiveGraph.Tests`).
- **mmap load allocates nothing on the managed heap** (0.0 MiB live and
  allocated) while a resident copy puts the whole file on the GC heap
  (+99.2 MiB at 1M nodes ≈ file size). This is the measured basis for the
  "very large graphs load without resident copies" claim; the mapped
  pages are file-backed and reclaimable by the OS.
