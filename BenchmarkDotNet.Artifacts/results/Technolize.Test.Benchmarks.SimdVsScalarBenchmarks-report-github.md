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
| SIMD_Implementation | Job-GLFQVB | InProcessEmitToolchain | 7.353 μs | 0.0749 μs | 0.0700 μs |  1.00 |    0.01 | 0.5569 |    9336 B |       1.000 |
| Scalar_Reference    | Job-GLFQVB | InProcessEmitToolchain | 8.484 μs | 0.0776 μs | 0.0726 μs |  1.15 |    0.01 | 0.5493 |    9288 B |       0.995 |
| SIMD_InPlace        | Job-GLFQVB | InProcessEmitToolchain | 7.060 μs | 0.0186 μs | 0.0174 μs |  0.96 |    0.01 |      - |      48 B |       0.005 |
| Scalar_InPlace      | Job-GLFQVB | InProcessEmitToolchain | 8.065 μs | 0.0741 μs | 0.0693 μs |  1.10 |    0.01 |      - |         - |       0.000 |
|                     |            |                        |          |           |           |       |         |        |           |             |
| SIMD_Implementation | .NET 9.0   | Default                | 7.422 μs | 0.0546 μs | 0.0484 μs |  1.00 |    0.01 | 0.5569 |    9336 B |       1.000 |
| Scalar_Reference    | .NET 9.0   | Default                | 8.645 μs | 0.0653 μs | 0.0611 μs |  1.16 |    0.01 | 0.5493 |    9288 B |       0.995 |
| SIMD_InPlace        | .NET 9.0   | Default                | 6.954 μs | 0.0439 μs | 0.0411 μs |  0.94 |    0.01 |      - |      48 B |       0.005 |
| Scalar_InPlace      | .NET 9.0   | Default                | 8.080 μs | 0.1247 μs | 0.1167 μs |  1.09 |    0.02 |      - |         - |       0.000 |
