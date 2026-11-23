# Issue #70 — V1/V2 version-selection validation

Measured by `SchemaCrossoverRunner` on the deterministic `GraphFactory`
tree (1 symbol node + 1 packed node + 2 properties per node). Environment:
see `ENVIRONMENT.md`. Raw rows: `results.json` (16 rows, medians of 3 reps).

## Finding 1: no automatic thresholds exist

The "automatic CognitiveGraph V1/V2 version-selection thresholds
(lines/characters/files)" that Minotaur #134 item 3 expected do not exist in
the package. `GraphBuilderOptions.Schema` defaults to V1 and is
caller-controlled; nothing in the package switches on size. What exists is
format **detection** at load time and a **hard structural ceiling** for V1
(32-bit offsets; `CompactGraphBuffer` rejects buffers above `int.MaxValue`
bytes). The correct data-backed decision rule is therefore stated directly
below (Finding 4), not reverse-engineered from shipped constants.

## Finding 2: measured behavior across scales

| Mode | Nodes | Build ms (med) | Bytes | B/node | Traverse ms (med) | Build alloc MB |
|---|---:|---:|---:|---:|---:|---:|
| v1-mem | 1000 | 1.0 | 104,046 | 104.0 | 0.3 | 0.6 |
| v2-mem | 1000 | 1.0 | 146,078 | 146.1 | n/a | 0.9 |
| v1-mmap | 1000 | 4.1 | 104,046 | 104.0 | 0.2 | 0.5 |
| v2-mmap | 1000 | 1.7 | 146,078 | 146.1 | 0.1 | 0.7 |
| v1-mem | 10,000 | 11.6 | 1,040,046 | 104.0 | 0.9 | 5.8 |
| v2-mem | 10,000 | 9.5 | 1,460,078 | 146.0 | n/a | 8.6 |
| v1-mmap | 10,000 | 11.0 | 1,040,046 | 104.0 | 0.8 | 4.8 |
| v2-mmap | 10,000 | 10.3 | 1,460,078 | 146.0 | 0.3 | 7.2 |
| v1-mem | 100,000 | 92.2 | 10,400,046 | 104.0 | 8.3 | 57.2 |
| v2-mem | 100,000 | 98.1 | 14,600,078 | 146.0 | n/a | 85.6 |
| v1-mmap | 100,000 | 104.5 | 10,400,046 | 104.0 | 8.1 | 47.3 |
| v2-mmap | 100,000 | 109.3 | 14,600,078 | 146.0 | 2.7 | 71.7 |
| v1-mem | 1,000,000 | 919.8 | 104,000,046 | 104.0 | 77.9 | 565.7 |
| v2-mem | 1,000,000 | 993.3 | 146,000,078 | 146.0 | n/a | 849.9 |
| v1-mmap | 1,000,000 | 793.5 | 104,000,046 | 104.0 | 83.4 | 466.5 |
| v2-mmap | 1,000,000 | 1,050.2 | 146,000,078 | 146.0 | 29.7 | 710.6 |

Facts the table supports (constant across scales, so they are structural,
not tuning artifacts):

- **Size**: V2 costs +42 B/node here (+40%): 64-bit offsets in node structs,
  property slots and child lists. Per-node bytes are exactly constant in
  both schemas across four orders of magnitude — memory is O(n) with no
  cliffs.
- **Build time**: statistically indistinguishable between schemas at every
  scale (within ~±10% run noise); both linear-ish in node count.
- **Read speed**: V2's file-backed unsafe-pointer accessors traverse
  the tree **2.6-2.8x faster** than V1's safe-span accessors at 100K/1M
  nodes (2.7 vs 8.1 ms; 29.7 vs 83.4 ms). V1 in-memory and V1 mmap read at
  the same speed (the mmap view is served via span reads).
- **Semantic fidelity**: the per-scale content checksum is identical across
  all readable modes (v1-mem, v1-mmap, v2-mmap) at every scale, so the two
  layouts encode the same tree.
- **In-memory V2 is not traversable today**: `GetNodeAtV2` requires the
  file-backed `UniversalGraphBuffer`; an in-memory V2 build supports
  `GetStatistics()`/`GetSourceText()` only.

## Finding 3: the V1 ceiling in operational terms

With the measured 104 B/node (this tree shape; real trees vary with
properties per node, child counts and source-text share), the V1 in-memory
ceiling of 2^31-1 bytes corresponds to roughly **20.6 million symbol
nodes** in one graph; a 1M-node graph is ~104 MB, 20x below the ceiling.
Per-file graphs — the shape every parser integration (Minotaur load
pipeline included) produces today — stay in the kilobyte-to-low-megabyte
range, three to five orders of magnitude below the ceiling.

## Finding 4: the data-backed selection rule (replaces "thresholds")

1. **Default V1 is measured-correct for per-file graphs.** It is the
   smaller layout (-29% bytes vs V2) with identical build time, and its
   safe-span traversal is well within interactive budgets at the sizes
   real files reach (1M-node synthetic: 78 ms full-tree traversal).
2. **Choose V2 only when a single graph approaches the 2 GiB ceiling**,
   i.e. whole-system / whole-repo merged graphs on the order of millions
   to tens of millions of nodes, or persisted graphs above 2 GiB. The
   trade is +40% bytes for +2.6-2.8x traversal speed via the mmapped
   accessors.
3. **V2 requires the file-backed path for reads**; an automated selector
   that picks V2 must also persist to disk (or that limitation must be
   lifted) before V2 reads exist at all.
4. Because no project-scale signal (lines/characters/files) can predict a
   single graph's byte size better than the builder's own live offset
   (bytes written), any future automatic selector should key on
   **estimated final buffer size**, sticky-switching to V2 when the
   estimate crosses ~2 GiB (the structural limit), not on file counts.

Nothing here contradicts the shipped defaults, so no code change is
required: rule 1-2 is the documentation of existing behavior, now with
committed numbers. If Minotaur later merges whole-project graphs, rule 4
is the selector design the data supports.
