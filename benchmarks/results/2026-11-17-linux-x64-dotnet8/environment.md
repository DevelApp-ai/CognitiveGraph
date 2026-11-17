# Environment

| Item | Value |
|---|---|
| Date | 2026-11-17 |
| OS | Debian GNU/Linux 13 (trixie), x64 |
| CPU | Intel Xeon 6985P-C @ 2.30GHz, 2 physical / 2 logical cores (AVX-512) |
| .NET SDK | 8.0.428 |
| .NET Runtime | 8.0.34 (8.0.3426.52217), X64 RyuJIT, Workstation GC (non-concurrent) |
| BenchmarkDotNet | 0.14.0 |
| Job | ShortRun: 3 warmup + 3 iterations, 1 launch, unroll default |
| Library under test | DevelApp.CognitiveGraph @ main (commit of 2026-11-15 tarball, tests: 113 passing) |
| Globalization | Invariant mode (sandbox has no ICU package) |

Caveat: this is a shared/virtualized machine (2 cores, other tenants
possible); medians are indicative, not lab-grade. The `benchmarks.yml`
workflow can reproduce the run on standard GitHub-hosted runners
(ubuntu/windows/macos-latest) with the same short job for verification.

## How these results were produced

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
dotnet build -c Release
dotnet run -c Release --no-build --project CognitiveGraph.Benchmarks -- \
  --job short --filter '*GraphCreationBenchmarks*'   # (then each other suite)
dotnet run -c Release --no-build --project CognitiveGraph.Benchmarks -- \
  --memory --mode graph   --size 1000    # (each mode+size in a fresh process)
```

Raw BenchmarkDotNet markdown/CSV exports are the `*-report-github.md` /
`*-report.csv` files in this folder. The memory rows in
`memory-footprint.md` were produced by `MemoryFootprintRunner`, one fresh
process per row.
