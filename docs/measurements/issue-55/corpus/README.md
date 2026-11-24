# Issue #55 OSS ground-truth defect corpus

This directory holds the real-world half of the #55 detection-effectiveness
ground truth: pre-fix source snapshots mined from bug-fixing commits in
pinned open-source projects. The synthetic article-scale half (200+ rule
corpus programs with known injected defects, plus the precision/recall
protocol) is owned by Minotaur #252 under `Minotaur docs/measurements/issue-246/`.
This corpus is what issue #55 adds on top: defects that actually occurred
in production OSS code.

## Layout

- `manifest.json`: one record per corpus entry (see the contract below),
  per-project rejection counters, `classifierVersion: 3`, and the
  `maxSnapshotBytes` cap (256 KiB) used at emit time.
- `corpus-source.json`: the project pins (owner/repo, license, license
  source URL, `since`, fetch windows) this corpus was mined from.
- `files/<Owner>/<Repo>/<fixSha8>/<FileName>.cs`: the pre-fix snapshot of a
  file that contained a defect, taken at the parent of the bug-fixing commit.

Per-entry contract (enforced offline by `dotnet run --project
CognitiveGraph.DefectMining -- validate --corpus <this dir>`, and enforced
in CI by `CognitiveGraph.Tests.CorpusIntegrityTests`):

| Field | Meaning |
|---|---|
| `id` | sha256 over (project, fixSha, path); stable corpus entry id |
| `fixSha` / `preFixSha` | bug-fixing commit and its parent (snapshot source) |
| `path` | repository-relative path of the defect file |
| `category` | `Nullability`, `ArrayBounds`, `TypeSafety`, or `DivisionByZero` |
| `signals` | per-signal hit counts that fired on ADDED lines of the fix diff |
| `preFixRanges` / `postFixRanges` | hunk ranges around the defect site in the pre-fix/post-fix file (1-based lines) |
| `bytes` / `gitBlobSha` | UTF-8 byte count and git blob SHA-1 of the committed snapshot |
| `message` / `date` | subject line and commit date of the fix |

## How it was mined (classifier v3)

1. **Pins.** 15 OSS .NET projects pinned with `since: 2018-01-01`, license and
   license source recorded in `corpus-source.json`. The most recent
   244-1,200 commit subjects per project were scanned (11,410 subjects total).
2. **Message pass** (subject line only): a subject must contain a fix-family
   word (`fix`, `bugfix`, `crash`, `resolve`, `regression`, `correct`) plus
   exactly one category keyword (`null reference`, `out of range`,
   `invalid cast`, `divide by zero`, and close variants; two categories
   claimed at once is rejected as ambiguous). Merge-commit subjects and
   compiler-annotation sweeps (`fix nullable warnings`, `NRT makeover`,
   nullability-annotation annotations) are never selected: annotation sweeps
   annotate signatures project-wide rather than repair a defect.
   Yield: **61 candidates** across 12 projects.
3. **Patch pass**: for each candidate, the ADDED lines of its unified diff
   are scored per category with fixed signal regexes. A file enters the
   plan only when `modified`, on an admissible source path (`*.cs`, no
   `/obj/` or `/bin/`, no test/spec files), with a unique best category
   scoring at least `MinPatchScore = 2` AND that category agrees with the
   message hint. Yield: **16 entries** across 7 projects.

Signal families (added-line regexes, `Classifier.PatchSignals`):

| Category | Signals |
|---|---|
| Nullability | `null-check`, `is-null`, `null-coalescing`, `null-conditional`, `throw-if-null`, `is-null-or-empty`, `reference-equals-null`, `default-null` |
| ArrayBounds | `length-guard`, `count-guard`, `arg-out-of-range`, `index-check`, `bounds-word`, `clamp` |
| TypeSafety | `as-cast-checked`, `is-pattern`, `invalid-cast`, `get-type-check`, `typeof-compare` |
| DivisionByZero | `divisor-guard`, `by-zero-word`, `epsilon-guard` |

Every planned snapshot is fetched at `preFixSha` and byte-validated at
emit time: the refetching client records the GitHub-reported blob SHA, and
`emit` recomputes `sha1("blob <len>\0" + bytes)` from the downloaded text;
any mismatch rejects the snapshot (`fetch-bytes-mismatch`). Snapshots over
256 KiB are rejected (`oversize`). `validate` re-checks schema, signals,
ranges, license, recomputed blob SHA, and byte counts offline; it also
guards against silent BOM loss (snapshots that legitimately start with a
UTF-8 BOM, e.g. both Dapper entries).

## Yields and honest negatives

16 entries from 11,410 scanned subjects; 7 of 15 projects yield entries:

| Project | Scanned | Candidates | Entries | Categories |
|---|---|---|---|---|
| mbdavid/LiteDB | 1200 | 26 | 9 | 7 Null, 2 ArrayBounds |
| JamesNK/Newtonsoft.Json | 297 | 9 | 1 | Nullability |
| DapperLib/Dapper | 366 | 3 | 2 | Nullability |
| NLog/NLog | 1200 | 5 | 1 | Nullability |
| serilog/serilog | 529 | 3 | 1 | Nullability |
| icsharpcode/SharpZipLib | 261 | 1 | 1 | ArrayBounds |
| AngleSharp/AngleSharp | 1200 | 6 | 1 | Nullability |
| AutoFixture, moq, MediatR, Polly, Bogus, FluentValidation, Humanizer, xunit | 6357 | 8 | 0 | - |

Rejection counters (per `wanted-blobs.json`, summed): `unclassified-patch`
84 (no file in the diff passed the patch pass), `not-source` 47 (test files,
config, docs), `no-accepted-file` 45 (candidate committed but no single file
carried both enough signals and message agreement), `status:added` 22 (new
files have no pre-fix snapshot), `status:renamed` 3.

### Zero-yield categories and the known TypeSafety near-miss

- **TypeSafety: 0 entries.** One genuine fix was found by the message pass
  (FluentValidation `6e825d33`, "Extend use of variance into the internal
  API to resolve invalid cast exceptions"), but its non-test diffs are an
  interface-variance redesign: the added lines carry none of the TypeSafety
  patch signals, and the regression test that pinpoints the exception lives
  in a test file, excluded from corpus paths by design. It is documented
  here rather than admitted by hand-picking so the filter stays mechanical.
- **DivisionByZero: 0 candidates** in any scanned window (no subject passed
  message pass with a division keyword). This is the honest result for
  these 15 projects since 2018; it is not padded with borderline commits.
- The synthetic half (Minotaur #252) covers the rule families that are rare
  in real-world fix logs (type-safety casts, division guards), which is
  exactly why both halves exist.

## Triage protocol (scaffold for the measurement consumer)

When a detector (e.g. Minotaur.Labyrinth L2/L3 rules) is run against these
snapshots, the pre/post-fix ranges give the adjudication anchors:

- **True positive**: the detector reports a finding inside (or within a
  small, pre-declared slack of) a `preFixRanges` window of an entry, with
  the entry's category.
- **False positive**: any finding outside those windows, or a finding that
  the post-fix snapshot does not silence (the fix removed the defect, so a
  detector still flagging the fixed file is flagging non-defect code).
- **False negative**: an entry with no true-positive finding.
- Adjudication of borderline window hits (whitespace-only range drift,
  findings one hunk off) must be pre-declared before measurement, never
  tuned after seeing detector output; record the rule in the measurement
  results under Minotaur `docs/measurements/issue-246/`.

## Reproduction

Fetch phases are token-free over a raw working directory (the dump formats
are documented in `Pipeline.cs`); `mine` performs the same REST chain
(`GET /repos/{owner}/{repo}/commits`, `/commits/{sha}` with full patch,
`/contents` at the pre-fix SHA) with a `GITHUB_TOKEN`:

```bash
dotnet run --project CognitiveGraph.DefectMining -- mine   --project owner/repo --raw <dir> [--since 2018-01-01]
dotnet run --project CognitiveGraph.DefectMining -- plan-candidates --raw <dir> --project owner/repo
dotnet run --project CognitiveGraph.DefectMining -- plan-blobs      --raw <dir> --project owner/repo
dotnet run --project CognitiveGraph.DefectMining -- emit  --raw <dir> --out docs/measurements/issue-55/corpus
dotnet run --project CognitiveGraph.DefectMining -- validate --corpus docs/measurements/issue-55/corpus
```

`validate` must pass byte-identically in CI (see `CorpusIntegrityTests`).
