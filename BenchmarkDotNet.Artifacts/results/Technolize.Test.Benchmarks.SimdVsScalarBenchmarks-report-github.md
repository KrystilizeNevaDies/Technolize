```

BenchmarkDotNet v0.14.0, Ubuntu 24.04.3 LTS (Noble Numbat)
AMD EPYC 7763, 1 CPU, 4 logical and 2 physical cores
.NET SDK 9.0.305
  [Host]   : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2
  .NET 9.0 : .NET 9.0.9 (9.0.925.41916), X64 RyuJIT AVX2

Runtime=.NET 9.0  

```
| Method              | Job        | Toolchain              | Mean     | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------- |----------- |----------------------- |---------:|----------:|----------:|------:|-------:|----------:|------------:|
| SIMD_Implementation | Job-HTEVRD | InProcessEmitToolchain | 7.093 μs | 0.0617 μs | 0.0577 μs |  1.00 | 0.5493 |    9288 B |       1.000 |
| Scalar_Reference    | Job-HTEVRD | InProcessEmitToolchain | 8.630 μs | 0.0873 μs | 0.0817 μs |  1.22 | 0.5493 |    9288 B |       1.000 |
| SIMD_InPlace        | Job-HTEVRD | InProcessEmitToolchain | 7.064 μs | 0.0533 μs | 0.0499 μs |  1.00 |      - |      48 B |       0.005 |
| Scalar_InPlace      | Job-HTEVRD | InProcessEmitToolchain | 7.919 μs | 0.0039 μs | 0.0030 μs |  1.12 |      - |         - |       0.000 |
|                     |            |                        |          |           |           |       |        |           |             |
| SIMD_Implementation | .NET 9.0   | Default                | 7.153 μs | 0.0555 μs | 0.0519 μs |  1.00 | 0.5493 |    9288 B |       1.000 |
| Scalar_Reference    | .NET 9.0   | Default                | 8.696 μs | 0.0373 μs | 0.0349 μs |  1.22 | 0.5493 |    9288 B |       1.000 |
| SIMD_InPlace        | .NET 9.0   | Default                | 5.762 μs | 0.0505 μs | 0.0472 μs |  0.81 |      - |      48 B |       0.005 |
| Scalar_InPlace      | .NET 9.0   | Default                | 8.079 μs | 0.0419 μs | 0.0392 μs |  1.13 |      - |         - |       0.000 |
