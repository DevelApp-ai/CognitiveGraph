/*
 * CognitiveGraph - Zero-Copy Cognitive Graph for Advanced Code Analysis
 * Copyright (C) 2024 DevelApp-ai
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Exporters.Csv;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace CognitiveGraph.Benchmarks;

public static class Program
{
    public static void Main(string[] args)
    {
        // Console modes that are not BenchmarkDotNet benchmarks:
        //   --memory [--mode graph|naive|mmap|resident --size N]
        //      Measures peak working set / heap for one graph shape in this
        //      process (run each mode+size in a fresh process so the numbers
        //      are independent - see benchmarks/README.md).
        if (args.Length > 0 && args[0] == "--memory")
        {
            MemoryFootprintRunner.Run(args.Skip(1).ToArray());
            return;
        }

        //   --schema-crossover [--scales CSV --reps N --outdir DIR]
        //      Issue #70: V1 (Compact) vs V2 (Universal) schema selection
        //      across scales, in-memory and file-backed (see
        //      benchmarks/README.md and docs/measurements/issue-70/).
        if (args.Length > 0 && args[0] == "--schema-crossover")
        {
            SchemaCrossoverRunner.Run(args.Skip(1).ToArray());
            return;
        }

        // Default: run the BenchmarkDotNet suite. Job selection is done from
        // the command line (e.g. `--job short`); see benchmarks/README.md.
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(ConsoleLogger.Default)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddExporter(MarkdownExporter.GitHub)
            .AddExporter(CsvExporter.Default)
            .AddColumn(StatisticColumn.Median)
            .AddColumn(StatisticColumn.P90)
            .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(24));

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
    }
}
