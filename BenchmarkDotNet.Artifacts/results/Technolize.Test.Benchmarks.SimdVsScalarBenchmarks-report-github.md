```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 9.0.305
  [Host]   : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2

Runtime=.NET 9.0  

```
| Method              | Job        | Toolchain              | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------- |----------- |----------------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| SIMD_Implementation | Job-KHZEMT | InProcessEmitToolchain | 7.435 μs | 0.1350 μs | 0.1127 μs |  1.00 |    0.02 | 0.7019 |   11515 B |        1.00 |
| Scalar_Reference    | Job-KHZEMT | InProcessEmitToolchain | 8.510 μs | 0.0599 μs | 0.0560 μs |  1.14 |    0.02 | 0.5493 |    9288 B |        0.81 |
| SIMD_InPlace        | Job-KHZEMT | InProcessEmitToolchain | 6.504 μs | 0.0324 μs | 0.0287 μs |  0.87 |    0.01 | 0.1297 |    2225 B |        0.19 |
| Scalar_InPlace      | Job-KHZEMT | InProcessEmitToolchain | 8.034 μs | 0.1163 μs | 0.1088 μs |  1.08 |    0.02 |      - |         - |        0.00 |
|                     |            |                        |          |           |           |       |         |        |           |             |
| SIMD_Implementation | .NET 9.0   | Default                | 7.343 μs | 0.0408 μs | 0.0382 μs |  1.00 |    0.01 | 0.7019 |   11517 B |        1.00 |
| Scalar_Reference    | .NET 9.0   | Default                | 8.595 μs | 0.0517 μs | 0.0459 μs |  1.17 |    0.01 | 0.5493 |    9288 B |        0.81 |
| SIMD_InPlace        | .NET 9.0   | Default                | 6.522 μs | 0.0383 μs | 0.0339 μs |  0.89 |    0.01 | 0.1297 |    2220 B |        0.19 |
| Scalar_InPlace      | .NET 9.0   | Default                | 8.276 μs | 0.1055 μs | 0.0987 μs |  1.13 |    0.01 |      - |         - |        0.00 |
