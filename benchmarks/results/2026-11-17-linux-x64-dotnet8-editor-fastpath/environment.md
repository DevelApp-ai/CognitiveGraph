# Environment

Identical to `../2026-11-17-linux-x64-dotnet8/` (same sandbox, same day):
see that folder's `environment.md` for the full table (Debian 13 trixie,
Intel Xeon @ 2.30 GHz 2c/2t, .NET SDK 8.0.428 / runtime 8.0.34,
BenchmarkDotNet 0.14.0, `--job short`, invariant globalization).

Library under test: `fix/editor-copypatch-fast-path` branch — editor
copy-on-write fast path for issue #68 plus the stand-alone-insert fix;
tests: 127 passing at this commit.

Command:

```bash
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
dotnet build -c Release
dotnet run -c Release --no-build --project CognitiveGraph.Benchmarks -- \
  --job short --filter '*EditBenchmarks*'
```
