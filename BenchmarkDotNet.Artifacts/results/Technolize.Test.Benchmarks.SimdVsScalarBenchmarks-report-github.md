```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 9.0.305
  [Host]   : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2

Runtime=.NET 9.0  

```
| Method              | Job        | Toolchain              | Mean      | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------- |----------- |----------------------- |----------:|----------:|----------:|------:|-------:|----------:|------------:|
| SIMD_Implementation | Job-NKIRTI | InProcessEmitToolchain | 20.339 μs | 0.0509 μs | 0.0452 μs |  1.00 | 0.5493 |    9360 B |       1.000 |
| Scalar_Reference    | Job-NKIRTI | InProcessEmitToolchain |  8.567 μs | 0.0392 μs | 0.0348 μs |  0.42 | 0.5493 |    9288 B |       0.992 |
| SIMD_InPlace        | Job-NKIRTI | InProcessEmitToolchain | 19.602 μs | 0.0127 μs | 0.0112 μs |  0.96 |      - |      72 B |       0.008 |
| Scalar_InPlace      | Job-NKIRTI | InProcessEmitToolchain |  8.212 μs | 0.1224 μs | 0.1145 μs |  0.40 |      - |         - |       0.000 |
|                     |            |                        |           |           |           |       |        |           |             |
| SIMD_Implementation | .NET 9.0   | Default                | 25.007 μs | 0.0391 μs | 0.0346 μs |  1.00 | 0.5493 |    9360 B |       1.000 |
| Scalar_Reference    | .NET 9.0   | Default                |  8.619 μs | 0.0536 μs | 0.0501 μs |  0.34 | 0.5493 |    9288 B |       0.992 |
| SIMD_InPlace        | .NET 9.0   | Default                | 19.596 μs | 0.0176 μs | 0.0147 μs |  0.78 |      - |      72 B |       0.008 |
| Scalar_InPlace      | .NET 9.0   | Default                |  8.139 μs | 0.0982 μs | 0.0919 μs |  0.33 |      - |         - |       0.000 |
