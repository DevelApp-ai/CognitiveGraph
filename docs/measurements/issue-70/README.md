# Issue #70 measurement harness: V1 (Compact) vs V2 (Universal) schema selection

Supports CognitiveGraph #70 (follow-up from DevelApp-ai/Minotaur #134 item 3),
which asks for validation of the "automatic CognitiveGraph V1/V2
version-selection thresholds".

## What the issue actually found (the premise)

There are no automatic version-selection thresholds in this package today.
The shipped selection policy is:

1. **Builder**: the caller picks the schema via `GraphBuilderOptions.Schema`;
   the default is **V1 (Compact)**. Nothing in the package switches schemas
   based on project size, line count, node count, edge count or byte size.
2. **Load**: the format self-describes (preamble magic + version), so
   `CognitiveGraph` detects V1/V2 from the buffer or file. This is detection,
   not selection.
3. **Hard ceiling**: V1 uses 32-bit offsets and an in-memory `byte[]`
   (capped at `int.MaxValue` bytes). `CompactGraphBuffer` rejects buffers
   past that mark with an explicit exception telling the caller to use V2.
   V2 (Universal) exists for graphs past that ceiling: 64-bit offsets and a
   memory-mapped-file backing store.
4. **Read access**: the V2 accessors (`SymbolNode64`, `GetNodeAtV2`) require
   the file-backed `UniversalGraphBuffer`. An in-memory V2 layout can be
   built and read for statistics, but not traversed with the V2 accessors —
   a selection-relevant fact this harness records.

Minotaur's #134 README said "the thresholds live in the CognitiveGraph
package"; they do not — that cross-reference is corrected by this
measurement. What follows gives the decision the data Minotaur #134 item 3
asked for.

## What the harness measures

`CognitiveGraph.Benchmarks/SchemaCrossoverRunner.cs`, console mode
`--schema-crossover`. Per symbol-node scale, the deterministic
`GraphFactory` tree is built through the public builder in both schemas and
measured in four modes:

| Mode | Schema | Storage | Build measured | Traversal measured |
|---|---|---|---|---|
| `v1-mem` | V1 | in-memory buffer (the default options path) | tree construction + final buffer | `GetNodeAt` |
| `v2-mem` | V2 | in-memory layout | tree construction + final buffer | n/a (V2 accessors need file backing) |
| `v1-mmap` | V1 | persisted file, loaded via memory-mapped ctor | tree construction + file I/O | `GetNodeAt` |
| `v2-mmap` | V2 | persisted file, loaded via memory-mapped ctor | tree construction + file I/O | `GetNodeAtV2` |

Per row: median build time over reps (fresh builder per rep), buffer/file
bytes, median warm traversal time over reps, build-phase managed
allocations, and a content checksum that must match across all readable
modes (semantic fidelity check between schemas).

## How to run (protocol)

One invocation per scale, fresh process each time (builder growth copies and
per-rep GC garbage peak at several times the final buffer size, so
accumulating large scales in one process can exceed container memory):

```bash
for scale in 1000 10000 100000 1000000; do
  dotnet run -c Release --project CognitiveGraph.Benchmarks -- \
    --schema-crossover --scale $scale --reps 3 --outdir docs/measurements/issue-70
done
```

Rows accumulate in `docs/measurements/issue-70/results.json` keyed by
(Mode, Nodes), so the last invocation finishes the dataset. Smaller scales
are safe to batch in one process via `--scales 1000,10000`.

A fast CI smoke variant runs the same code paths:

```bash
dotnet run -c Release --project CognitiveGraph.Benchmarks -- \
  --schema-crossover --scale 1000 --reps 1
```
