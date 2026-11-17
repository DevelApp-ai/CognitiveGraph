```

BenchmarkDotNet v0.14.0, Debian GNU/Linux 13 (trixie)
INTEL XEON PLATINUM 8581C CPU 2.30GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 8.0.428
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                   | Mean          | Error         | StdDev       | Median        | P90           | Gen0       | Gen1       | Gen2       | Allocated    |
|----------------------------------------- |--------------:|--------------:|-------------:|--------------:|--------------:|-----------:|-----------:|-----------:|-------------:|
| Graph_SustainedEdits_100                 | 191,720.85 μs |  95,533.47 μs | 5,236.512 μs | 189,538.83 μs | 196,064.19 μs | 24000.0000 | 24000.0000 | 24000.0000 | 221099.75 KB |
| Graph_SustainedPropertyUpdates_100       |  54,327.88 μs |  15,727.51 μs |   862.078 μs |  53,973.03 μs |  55,043.21 μs | 16555.5556 | 16555.5556 | 16555.5556 |    101828 KB |
| PersistentTree_SustainedPathRebuilds_100 |      59.81 μs |      79.46 μs |     4.355 μs |      61.97 μs |      62.52 μs |     1.2207 |          - |          - |    109.05 KB |
| NaiveTree_SustainedRebuilds_100          | 100,316.66 μs | 171,433.05 μs | 9,396.824 μs | 103,470.69 μs | 106,878.71 μs |  4250.0000 |  2000.0000 |          - | 347658.77 KB |
