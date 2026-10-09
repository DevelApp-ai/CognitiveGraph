# CognitiveGraph Consumer Contract

**Status:** contract for code-analysis consumers (SpecTreeGenerator, Golem) —
CognitiveGraph issue #53, cross-repo tracking DevelApp-ai/SpecTreeGenerator#18
(Gaps 3 + 5).

Consumers build semantic relationship edges (spec trees, translations)
directly from CognitiveGraph nodes and edges. This document states the
guarantees the graph format provides, and the conventions producers must
follow so consumers can rely on them.

## 1. Node identity across files and edits

- Every `SymbolNode` carries a `SymbolID` (and `NodeType`) that is **stable
  across parses of identical source** and **preserved by
  `CognitiveGraphEditor` edit operations**: `InsertNode`, `ReplaceNode`,
  `DeleteNode`, `MoveNode` and property updates never renumber existing
  nodes' `SymbolID`s; re-serialized graphs (clones, incremental edit results)
  keep symbol identity for unchanged declarations.
- Edges (`CpgEdgeData`) reference targets **by node offset within the same
  graph buffer**. Offsets are internal; consumers must treat `SymbolID` +
  declaration properties (e.g. `Name`, `Kind`) as the durable identity, and
  re-read edges via accessors (`CpgEdge.GetTargetNode()`) after any edit, so
  re-targeting is invisible to them.
- Cross-file identity is **not** an offset: producers assign consistent
  `SymbolID`s per symbol kind and record the qualified name in properties
  (see §3); the stable cross-file key is `project|qualifiedName` as produced
  by the StepParser symbol table (`SymbolRef`).

## 2. Span granularity

- Every node carries `SourceStart`/`SourceLength` covering the node's full
  syntactic extent (e.g. an entire class declaration).
- For declaration nodes, producers **must additionally record the identifier
  span** — the exact extent of the declared *name* — using the well-known
  properties:
  - `IdentifierStart` (UInt32): offset of the declared name's first character.
  - `IdentifierLength` (UInt32): length in characters of the declared name.
- The identifier span is guaranteed to be contained within the declaration
  node's `SourceStart..SourceStart+SourceLength`.
- Consumers anchoring documentation or renames must use the identifier span,
  never the declaration span (which would anchor to `public class ...`).

## 3. Declaration properties schema

Declaration nodes that participate in semantic relationships carry:

| Property | Type | Meaning |
|---|---|---|
| `Name` | String | The raw declared name. |
| `Kind` | String | Declaration kind: `class`, `interface`, `struct`, `method`, `field`, `property`, ... |
| `IdentifierStart` / `IdentifierLength` | UInt32 | See §2. |
| `QualifiedName` | String | Optional: container-qualified name for cross-file identity. |

## 4. CPG edge types for declaration relationships

The `EdgeType` enum provides the semantic edge set needed for spec-tree
extraction:

| Edge | Value | From → To |
|---|---|---|
| `AST_CHILD` | 1 | Syntactic parent → child (per chosen parse) |
| `CONTROL_FLOW` | 2 | Statement → successor statement |
| `DATA_FLOW` | 3 | Definition → use |
| `CALLS` | 4 | CallExpression → FunctionDeclaration |
| `TYPE` | 5 | Expression/annotation → type node |
| `INHERITS` | 6 | Derived type declaration → base class declaration |
| `IMPLEMENTS` | 7 | Type declaration → interface declaration |
| `USES_TYPE` | 8 | Declaration/use site → type used in signature or body (field/property/parameter/return/local types, instantiations) |

Producers of declaration edges should attach a `relation` (String) property
when the edge admits sub-kinds (e.g. `base` vs `interface`).

## 5. Test guarantees

`CognitiveGraph.Tests/ConsumerContractTests.cs` proves, on a graph built with
the public builder API:

- identifier spans are carried and are strictly narrower than the enclosing
  declaration span;
- `INHERITS`/`IMPLEMENTS` edges target the correct declaration nodes with
  edge properties;
- node identity (SymbolID, spans, edge targets) is preserved through
  `CognitiveGraphEditor.Build()` clone/rebuild;
- the new edge types are distinct wire values (6, 7, 8).
