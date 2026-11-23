# Issue #70 measurement environment

Recorded per the #27 ground rules (Research-article-builder#27) so the
committed numbers are reproducible and honestly scoped.

- Date: 2026-11-23
- Host: DevelApp dev container (CI-grade), Debian GNU/Linux 13 (trixie), x86_64
- CPUs: 2 vCPU; memory: ~3.1 GB (this cap is why the protocol runs one scale
  per process; see the harness notes in `benchmarks/README.md`)
- .NET SDK 8.0.428; runtime Microsoft.NETCore.App 8.0.34; Release build,
  no benchmark framework (plain `Stopwatch`, `dotnet run -c Release`)
- Repository state: CognitiveGraph `main` at `a4881ba` plus this branch's
  harness addition (`CognitiveGraph.Benchmarks/SchemaCrossoverRunner.cs`); no
  library code was changed for the measurement.
- Repetitions: 3 per data point (fresh builder per rep, full GC between
  reps); reported values are medians. Traversal is measured on warm
  accessors after one untimed warm pass.
- Determinism: the graph corpus is `GraphFactory`'s fixed tree shape
  (complete binary tree, one symbol node + one packed node + two properties
  per node, source text 8 bytes per node); content is fully deterministic,
  no seeds needed. Content fidelity is checked per scale by a checksum that
  must match across every readable mode (see `RESULTS.md`).
- Storage: local container filesystem (temp dir) for the file-backed modes.
