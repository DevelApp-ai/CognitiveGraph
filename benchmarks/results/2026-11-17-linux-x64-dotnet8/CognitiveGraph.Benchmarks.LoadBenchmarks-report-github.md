```

BenchmarkDotNet v0.14.0, Debian GNU/Linux 13 (trixie)
Intel Xeon 6985P-C CPU 2.30GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 8.0.428
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | Mean        | Error      | StdDev    | Median      | P90         | Gen0     | Gen1     | Gen2     | Allocated   |
|--------------------------- |------------:|-----------:|----------:|------------:|------------:|---------:|---------:|---------:|------------:|
| Load_MemoryMapped_Traverse | 14,105.1 μs | 3,439.5 μs | 188.53 μs | 14,000.5 μs | 14,258.3 μs |        - |        - |        - |     1.96 KB |
| Load_MemoryMapped_Open     |    313.2 μs |   108.0 μs |   5.92 μs |    310.5 μs |    318.1 μs |        - |        - |        - |     1.92 KB |
| Load_ResidentCopy_Traverse | 11,246.1 μs | 4,660.5 μs | 255.46 μs | 11,119.2 μs | 11,456.0 μs | 984.3750 | 984.3750 | 984.3750 | 10158.86 KB |
