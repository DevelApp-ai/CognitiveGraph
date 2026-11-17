```

BenchmarkDotNet v0.14.0, Debian GNU/Linux 13 (trixie)
INTEL XEON PLATINUM 8581C CPU 2.30GHz, 1 CPU, 2 logical and 2 physical cores
.NET SDK 8.0.428
  [Host]   : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  ShortRun : .NET 8.0.34 (8.0.3426.52217), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                     | NodeCount | Mean               | Error             | StdDev          | Median             | P90                | Gen0     | Gen1     | Gen2     | Allocated  |
|--------------------------- |---------- |-------------------:|------------------:|----------------:|-------------------:|-------------------:|---------:|---------:|---------:|-----------:|
| **Graph_ReplaceSingleNode**    | **1000**      |    **157,685.6320 ns** |   **137,417.1110 ns** |   **7,532.2956 ns** |    **153,905.0913 ns** |    **163,868.5542 ns** |  **32.2266** |  **32.2266** |  **32.2266** |   **215455 B** |
| Graph_UpdateProperty       | 1000      |     46,189.6767 ns |    25,330.5526 ns |   1,388.4531 ns |     45,493.3275 ns |     47,329.4665 ns |  32.2266 |  32.2266 |  32.2266 |   106591 B |
| PersistentTree_PathRebuild | 1000      |        440.4461 ns |        99.2917 ns |       5.4425 ns |        441.0645 ns |        444.6553 ns |   0.0105 |        - |        - |      880 B |
| NaiveObject_InPlaceEdit    | 1000      |          0.0167 ns |         0.5275 ns |       0.0289 ns |          0.0000 ns |          0.0401 ns |        - |        - |        - |          - |
| **Graph_ReplaceSingleNode**    | **10000**     |  **1,944,975.4258 ns** |   **787,800.3264 ns** |  **43,181.9944 ns** |  **1,944,170.1992 ns** |  **1,979,677.5617 ns** | **250.0000** | **250.0000** | **250.0000** |  **2276406 B** |
| Graph_UpdateProperty       | 10000     |    724,470.6859 ns |    88,934.5790 ns |   4,874.8044 ns |    726,806.2314 ns |    727,551.8846 ns | 166.0156 | 166.0156 | 166.0156 |  1042718 B |
| PersistentTree_PathRebuild | 10000     |        623.7145 ns |       281.0269 ns |      15.4040 ns |        617.3898 ns |        636.4973 ns |   0.0153 |        - |        - |     1320 B |
| NaiveObject_InPlaceEdit    | 10000     |          0.1158 ns |         0.1985 ns |       0.0109 ns |          0.1118 ns |          0.1249 ns |        - |        - |        - |          - |
| **Graph_ReplaceSingleNode**    | **100000**    | **24,584,160.8646 ns** | **7,105,435.9602 ns** | **389,472.9228 ns** | **24,472,620.1250 ns** | **24,908,312.0500 ns** | **250.0000** | **250.0000** | **250.0000** | **21949014 B** |
| Graph_UpdateProperty       | 100000    |  5,929,436.9570 ns | 9,902,475.5589 ns | 542,788.1020 ns |  5,834,117.0039 ns |  6,377,680.2602 ns | 164.0625 | 164.0625 | 164.0625 | 10402724 B |
| PersistentTree_PathRebuild | 100000    |        785.4192 ns |       245.4611 ns |      13.4546 ns |        786.8217 ns |        795.8583 ns |   0.0181 |        - |        - |     1584 B |
| NaiveObject_InPlaceEdit    | 100000    |          0.0092 ns |         0.1616 ns |       0.0089 ns |          0.0098 ns |          0.0161 ns |        - |        - |        - |          - |
