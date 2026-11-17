```

BenchmarkDotNet v0.14.0, Debian GNU/Linux 13 (trixie)
Intel Xeon 6985P-C CPU 2.30GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 8.0.428
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                         | Mean         | Error         | StdDev        | Median       | P90          | Gen0   | Allocated |
|------------------------------- |-------------:|--------------:|--------------:|-------------:|-------------:|-------:|----------:|
| Graph_HeaderAccess             |    16.393 μs |     10.814 μs |     0.5928 μs |    16.128 μs |    16.883 μs |      - |         - |
| Graph_TryGetPropertyByKey      | 2,747.411 μs | 30,366.443 μs | 1,664.4872 μs | 1,839.318 μs | 4,102.617 μs | 7.8125 |  720003 B |
| Graph_SpanPropertyEnumeration  |   371.452 μs |    103.231 μs |     5.6584 μs |   370.949 μs |   376.066 μs |      - |       1 B |
| Naive_FieldAccess              |     7.094 μs |      3.257 μs |     0.1786 μs |     7.174 μs |     7.209 μs |      - |         - |
| Naive_DictionaryPropertyLookup |   139.000 μs |     10.024 μs |     0.5495 μs |   138.851 μs |   139.458 μs |      - |         - |
