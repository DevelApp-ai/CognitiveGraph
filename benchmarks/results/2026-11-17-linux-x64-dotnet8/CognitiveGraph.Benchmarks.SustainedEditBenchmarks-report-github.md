```

BenchmarkDotNet v0.14.0, Debian GNU/Linux 13 (trixie)
Intel Xeon 6985P-C CPU 2.30GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 8.0.428
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                   | Mean          | Error         | StdDev        | Median        | P90           | Gen0       | Gen1       | Gen2       | Allocated     |
|----------------------------------------- |--------------:|--------------:|--------------:|--------------:|--------------:|-----------:|-----------:|-----------:|--------------:|
| Graph_SustainedEdits_100                 | 969,026.31 μs |  11,859.80 μs |    650.076 μs | 969,274.95 μs | 969,467.29 μs | 79000.0000 | 79000.0000 | 79000.0000 | 1001903.34 KB |
| Graph_SustainedPropertyUpdates_100       | 859,608.79 μs | 467,859.05 μs | 25,644.933 μs | 872,143.78 μs | 875,688.97 μs | 79000.0000 | 79000.0000 | 79000.0000 | 1001911.16 KB |
| PersistentTree_SustainedPathRebuilds_100 |      50.65 μs |      19.02 μs |      1.042 μs |      50.56 μs |      51.50 μs |     1.2207 |          - |          - |     109.05 KB |
| NaiveTree_SustainedRebuilds_100          |  86,026.22 μs |  47,437.07 μs |  2,600.186 μs |  86,134.50 μs |  88,083.36 μs |  4200.0000 |  2000.0000 |          - |  347658.74 KB |
