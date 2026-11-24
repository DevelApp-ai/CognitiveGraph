# Issue #55 corpus mining environment

Recorded per the #27 ground rules (Research-article-builder#27) so the
committed corpus is reproducible and honestly scoped.

- Date: 2026-11-23/24 (fetch and emit)
- Host: DevelApp dev container (CI-grade), Debian GNU/Linux 13 (trixie), x86_64
- .NET SDK 8.0.428; Release build; `InvariantGlobalization` true for the
  mining tool
- Tool: `CognitiveGraph.DefectMining` (new console project in this branch),
  classifier version **3** as recorded in `manifest.json`
- Fetch path: authenticated GitHub REST via the documented dump formats
  (`mine` performs the identical REST chain with a `GITHUB_TOKEN`):
  `GET /repos/{owner}/{repo}/commits` (paged, `since` bounded),
  `GET /repos/{owner}/{repo}/commits/{sha}` (full patch),
  `GET /repos/{owner}/{repo}/contents/{path}` at the pre-fix SHA
  (content + reported blob SHA). Blob snapshots are byte-validated in
  `emit` by recomputing `sha1("blob <len>\0" + bytes)` against the reported
  blob SHA (BOM-inclusive), so transcodings cannot enter the corpus.
- Scan windows: `since: 2018-01-01`, most recent up to 1,200 subjects per
  project (11,410 subjects across 15 pinned projects). Heads at fetch:
  AngleSharp `98b66dc92` (2026-11-23), Polly `ad7d87bf1` (2026-11-21),
  AutoFixture `7f5cc7e71` (2026-09-06), Dapper `188094784` (2026-11-20),
  FluentValidation `5903fa658` (2026-11-13), Humanizer `4574b9388` (2026-10-08),
  Newtonsoft.Json `52fa3aef1` (2026-09-21), NLog `54ebce298` (2026-11-23),
  Bogus `6ece18c5c` (2025-12-22), SharpZipLib `ff2d7c30b` (2023-08-12),
  MediatR `1fad4c7a3` (2026-11-12), LiteDB `b3c440623` (2026-10-26),
  moq `bdf16e758` (2026-10-14), serilog `43105b350` (2026-10-23),
  xunit `ad8d0dd14` (2026-11-19). Repos are live projects; the corpus is
  the emitted, byte-validated state at these heads, not a moving target.
- Licenses: MIT (Newtonsoft.Json, Humanizer, AutoFixture, Bogus,
  SharpZipLib, LiteDB, AngleSharp), Apache-2.0 (Dapper, serilog,
  FluentValidation, MediatR, xunit), BSD-3-Clause (NLog, moq, Polly);
  per-project license source URLs recorded in `corpus-source.json`.
- Reproduction: see `README.md`; `validate` re-checks the committed corpus
  offline and in CI (`CorpusIntegrityTests`), including the 256 KiB
  `maxSnapshotBytes` cap and byte-exact blob SHA recomputation.
