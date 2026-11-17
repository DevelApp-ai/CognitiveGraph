# Edit-path re-measurement — editor copy-on-write fast path (issue #68)

Same environment as `../2026-11-17-linux-x64-dotnet8/` (see its
`environment.md`), same day, same `--job short` protocol. The only change
under test is the editor fast path from #68: layout-preserving edits
(`ReplaceNode` without packed-node/property overrides, `MoveNode`,
same-length `UpdateProperty`) now clone the source buffer once and patch
fixed-size records in place, instead of re-emitting every node through a
`CognitiveGraphBuilder`. Span-changing patches additionally rebuild the
serialized interval tree in place (same node count → same size). All other
operations fall back to the full rebuild.

Test suite at this commit: 127 passing (113 pre-existing + 14 new
`EditorFastPathTests`).

## Before → after (medians, same machine, same job)

| Benchmark | Before (full rebuild) | After (fast path) | Improvement |
|---|---|---|---|
| ReplaceSingleNode @1K | 1.970 ms / 1.02 MB | 0.154 ms / 215 KB | 12.8× / 4.8× |
| ReplaceSingleNode @10K | 23.97 ms / 10.26 MB | 1.944 ms / 2.28 MB | 12.3× / 4.5× |
| ReplaceSingleNode @100K | 292.7 ms / 113.2 MB | 24.47 ms / 21.9 MB | 11.9× / 5.2× |
| UpdateProperty @1K | 2.054 ms / 1.02 MB | 0.046 ms / 107 KB | 44.9× / 9.6× |
| UpdateProperty @10K | 24.29 ms / 10.26 MB | 0.727 ms / 1.04 MB | 33.4× / 9.9× |
| UpdateProperty @100K | 314.4 ms / 113.2 MB | 5.834 ms / 10.4 MB | 53.9× / 10.9× |
| SustainedEdits_100 (100 rewrites @10K) | 969.3 ms / 978 MB | 189.5 ms / 216 MB | 5.1× / 4.5× |
| SustainedPropertyUpdates_100 (@10K) | 872.1 ms / 978 MB | 54.0 ms / 99.4 MB | 16.2× / 9.8× |

Gen0/Gen1/Gen2 collections per 1000 sustained-loop ops drop from 79,000 to
24,000 (SustainedEdits_100) and 16,556 (SustainedPropertyUpdates_100).

## What the numbers say now

- A layout-preserving edit is a single `memcpy` of the graph buffer
  (source text included) plus O(ops) patching — allocation per edit is one
  flat buffer-sized array, not per-node garbage (no boxing, no builder).
  Property-only edits skip the interval-tree rewrite and additionally beat
  that: 1.04 MB allocated @10K is just the cloned buffer.
- Span-changing edits (ReplaceSingleNode/MoveNode) add an O(n log n)
  in-place rebuild of the serialized interval tree (~1.9 ms vs 0.7 ms
  median for the property-only path @10K).
- Because each result buffer is a fresh large-object array, a sustained
  edit loop still triggers Gen0/Gen1/Gen2 collections (24 per 100-edit op
  vs 79 before). Making edits truly proportional-to-change would need
  structural sharing (offset indirection between versions), which is out
  of scope for #68 and tracked as a possible follow-up.
- The persistent-tree baseline remains faster for a single path edit
  (0.62 µs @10K vs 1.94 ms): O(depth) vs O(buffer). The honest claim for
  the article is "a layout-preserving edit costs one buffer copy, with no
  per-node allocations", not "proportional to the change".

## Files

- `CognitiveGraph.Benchmarks.EditBenchmarks-report-github.md` / `.csv`
- `CognitiveGraph.Benchmarks.SustainedEditBenchmarks-report-github.md` / `.csv`

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
dotnet build -c Release
dotnet run -c Release --no-build --project CognitiveGraph.Benchmarks -- \
  --job short --filter '*EditBenchmarks*'   # runs both edit suites (16 benchmarks)
```
