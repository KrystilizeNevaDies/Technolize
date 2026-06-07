# Technolize SIMD Performance Benchmarks

This document describes the integrated benchmarking framework for Technolize's SIMD signature processing implementation.

## Overview

We've integrated [BenchmarkDotNet](https://benchmarkdotnet.org/) to provide comprehensive performance analysis of the SIMD-optimized signature computation in Technolize. The benchmarks focus on the core `SignatureProcessor` class and the `SignatureWorldTicker` that uses it.

## Running Benchmarks

### Prerequisites
- .NET 8.0 SDK
- Release configuration build (required for accurate performance measurements)

### Commands

```bash
# Build solution in Release mode (required)
dotnet build -c Release

# Run all benchmarks
dotnet run --project Technolize.Test -c Release -- benchmark

# Run specific benchmark categories
dotnet run --project Technolize.Test -c Release -- benchmark --filter "*SignatureProcessor*"
dotnet run --project Technolize.Test -c Release -- benchmark --filter "*SignatureWorldTicker*"
dotnet run --project Technolize.Test -c Release -- benchmark --filter "*SimdVsScalar*"
dotnet run --project Technolize.Test -c Release -- benchmark --filter "*WorldRenderer*"
```

> The `*WorldRenderer*` graphics benchmarks open a real offscreen OpenGL 4.6 context, so they only
> run on a machine with a usable display/GPU (the same requirement as the `render-snapshot` gate).

## Benchmark Categories

### 1. SignatureProcessor Benchmarks (`SignatureProcessorBenchmarks`)
Tests the core SIMD signature computation with different data sizes:
- **SmallData_SingleSignature_3x3**: Baseline single signature computation
- **SmallData_AllSignatures_3x3**: Full 3x3 signature computation
- **MediumData_AllSignatures_16x16**: Medium-sized data (16x16)
- **LargeData_AllSignatures_64x64**: Large data (64x64)
- **RegionSizeData_AllSignatures_34x34**: World region size (32+2 padding)
- **RegionSizeData_InPlace_Span**: In-place computation using spans
- **LargeData_InPlace_Span**: Large data in-place computation
- **MultiSeed_RegionSize**: Multiple seeds performance
- **VectorWidth_Info**: Shows SIMD vector width capability

### 2. SIMD vs Scalar Comparison (`SimdVsScalarBenchmarks`)
Direct comparison between SIMD and scalar implementations:
- **SIMD_Implementation**: Production SIMD code
- **Scalar_Reference**: Reference scalar implementation
- **SIMD_InPlace**: SIMD with pre-allocated buffers
- **Scalar_InPlace**: Scalar with pre-allocated buffers

### 3. SignatureWorldTicker Benchmarks (`SignatureWorldTickerBenchmarks`)
Tests the complete world ticking performance:
- **SmallWorld_SingleTick**: 1x1 regions (32x32 blocks)
- **MediumWorld_SingleTick**: 2x2 regions (64x64 blocks)
- **LargeWorld_SingleTick**: 4x4 regions (128x128 blocks)
- **SmallWorld_MultipleTicks**: Multiple consecutive ticks
- **MediumWorld_MultipleTicks**: Multiple ticks on medium world
- **SmallWorld_MemoryStress**: Memory allocation patterns
```

## Benchmark Categories

### 1. SignatureProcessor Benchmarks (`SignatureProcessorBenchmarks`)
Tests the core SIMD signature computation with different data sizes:
- **SmallData_SingleSignature_3x3**: Baseline single signature computation
- **SmallData_AllSignatures_3x3**: Full 3x3 signature computation
- **MediumData_AllSignatures_16x16**: Medium-sized data (16x16)
- **LargeData_AllSignatures_64x64**: Large data (64x64)
- **RegionSizeData_AllSignatures_34x34**: World region size (32+2 padding)
- **RegionSizeData_InPlace_Span**: In-place computation using spans
- **LargeData_InPlace_Span**: Large data in-place computation
- **MultiSeed_RegionSize**: Multiple seeds performance
- **VectorWidth_Info**: Shows SIMD vector width capability

### 2. SIMD vs Scalar Comparison (`SimdVsScalarBenchmarks`)
Direct comparison between SIMD and scalar implementations:
- **SIMD_Implementation**: Production SIMD code
- **Scalar_Reference**: Reference scalar implementation
- **SIMD_InPlace**: SIMD with pre-allocated buffers
- **Scalar_InPlace**: Scalar with pre-allocated buffers

### 3. SignatureWorldTicker Benchmarks (`SignatureWorldTickerBenchmarks`)
Tests the complete world ticking performance:
- **SmallWorld_SingleTick**: 1x1 regions (32x32 blocks)
- **MediumWorld_SingleTick**: 2x2 regions (64x64 blocks)
- **LargeWorld_SingleTick**: 4x4 regions (128x128 blocks)
- **SmallWorld_MultipleTicks**: Multiple consecutive ticks
- **MediumWorld_MultipleTicks**: Multiple ticks on medium world
- **SmallWorld_MemoryStress**: Memory allocation patterns

### 4. Graphics Rendering Benchmarks (`WorldRendererBenchmarks`)
Measures the Silk.NET OpenGL 4.6 world shader render path on the same deterministic scene the
`render-snapshot` gate uses (a 1280x720 water body with a stone floor, pillars, and air pockets that
exercise the shader's reflection/refraction/escape paths). Two phases are reported separately:
- **CPU: build SSBO + colour resources**: `WorldShaderResourceBuilder.Build` only — serialises the
  world quadtree into the SSBO node array and packs the dense colour texture (no GPU calls).
- **GPU: render per frame (1280x720, no readback)**: `WorldShaderRenderer.RenderToTarget` — uploads
  the resources and runs `world_renderer.frag` into an offscreen framebuffer. It renders 30 frames per
  invocation (`OperationsPerInvoke = 30`) so the reported **Mean is the per-frame render cost**, and
  deliberately omits the GPU→CPU pixel readback (the synchronous `glReadPixels` stall the snapshot path
  pays); a single `glFinish` after the batch ensures all queued frames complete before timing stops.

The `render-snapshot` command remains the correctness gate (pixel-exact compare against
`render-snapshots/baseline.png`). It also prints the GL renderer identity and timing: per-frame
`MeanMs`/`P95Ms`/`MeanFps` plus `GpuDrawMeanMs` (GPU shader-execution time only, via a
`GL_TIME_ELAPSED` query — excludes the CPU-side readback). An optional `--sun-rays N` flag overrides
the sun ray count to measure the lighting perf/quality tradeoff against the committed baseline:
```bash
dotnet run --project Technolize.Test -c Release -- render-snapshot 30
dotnet run --project Technolize.Test -c Release -- render-snapshot 30 --sun-rays 9
```

### Render performance diagnostics

Two diagnostic commands save image sets and tabulate the perf/quality tradeoff of the render path:

- **`render-sun-sweep [maxRays]`** — renders the scene at every sun ray count `1..maxRays` (default 64),
  saving `render-snapshots/sun-sweep/rays-NN.png` and reporting each one's diff from the max-ray
  reference. Used to choose the default `WorldLighting.SunRayCount`.
- **`render-scale-scan [sunRays]`** — sweeps `WorldShaderRenderer.LightingResolutionScale` (the two-pass
  low-resolution lighting path), saving `render-snapshots/lighting-scan/scale-*.png` and reporting GPU
  time / estimated FPS and the pixel diff from the exact single-pass (full-resolution lighting) render.

The world shader lighting is the dominant per-pixel cost (an expensive sun march per fragment). By
default it runs at **full resolution and full ray count** with **sparse temporal refresh**: each frame
only a `WorldShaderRenderer.PixelUpdateFraction` slice of the screen (a contiguous horizontal band,
selected with the GPU scissor so the skipped pixels cost nothing) recomputes its full-ray lighting, and
the rest of a persistent lighting buffer is carried forward. The band sweeps over
~`1 / PixelUpdateFraction` frames, so a static view converges to the **exact** full-quality image
(pixel-identical to relighting everything) while each frame costs roughly that fraction of the full
lighting work. Measured on the profile scene (RTX 2060, full 9 rays), GPU lighting time scales nearly
linearly with the fraction down to an ~8 ms composite floor:

| PixelUpdateFraction | GPU ms | ~FPS | Converged diff |
|---:|---:|---:|---:|
| 1.0 (relight all) | 202 | 5 | 0 |
| 0.25 | 54 | 18 | 0 |
| 0.125 (default) | 29 | 34 | 0 |
| 0.0625 | 17 | 58 | 0 |
| 0.02 | 8.4 | 119 | 0 |

Tuning knobs on `WorldShaderRenderer`:
- `EnableTemporalAccumulation` (default `true`) — turn the sparse-refresh path on/off.
- `PixelUpdateFraction` (default `0.125`) — fraction of the screen relit per frame. Lower = cheaper
  frames and higher FPS, but the lighting takes more frames to refresh, so a moving view trails by up to
  one sweep (visible as a brief horizontal lag during motion); converges exactly when still.
- `LightingResolutionScale` (default `1.0` = full) — render lighting below full resolution to trade
  sharpness for speed (bilinearly upsampled during the composite); composes with the fraction knob.

`render-pixel-sweep` reproduces the table above and saves a converged image per fraction:
```bash
dotnet run --project Technolize.Test -c Release -- render-pixel-sweep
dotnet run --project Technolize.Test -c Release -- render-sun-sweep 64
dotnet run --project Technolize.Test -c Release -- render-scale-scan 9
```


The benchmarks measure:
- **Mean execution time**: Average time per operation
- **Memory allocations**: Managed memory allocations per operation
- **Standard deviation**: Performance consistency
- **Allocation ratios**: Memory efficiency comparisons
- **GC pressure**: Garbage collection impact

## SIMD Hardware Information

The benchmarks automatically detect and report:
- Vector width (e.g., AVX2 = 256-bit vectors)
- Hardware intrinsics available
- Processor architecture
- .NET runtime version

## Initial Performance Insights

Based on initial benchmarking results on AMD EPYC 7763 with AVX2:

### SIMD vs Scalar Performance
- **Scalar in-place operations**: ~8.2 μs (fastest)
- **SIMD in-place operations**: ~19.6 μs 
- **Memory overhead**: SIMD allocates ~72B vs 0B for scalar in-place

### Key Findings
1. For small data sizes (34x34), scalar implementation shows better performance
2. SIMD implementation has memory allocation overhead
3. In-place operations significantly reduce allocations
4. Performance varies between different execution environments

### Memory Efficiency
- In-place operations avoid most allocations
- SIMD operations can have vector alignment overhead
- Scalar operations have minimal memory footprint

## Optimization Opportunities

The benchmarks reveal several optimization areas:
1. **Data size threshold**: Determine minimum data size where SIMD becomes beneficial
2. **Memory alignment**: Optimize vector operations for better cache utilization  
3. **Batch processing**: Process multiple regions together for better SIMD utilization
4. **Algorithm tuning**: Adjust SIMD implementation for specific data patterns

## Understanding Results

### When SIMD Excels
- Large data sets (>64x64)
- Batch processing multiple regions
- When memory allocations are amortized

### When Scalar Excels  
- Small data sets (<32x32)
- Single operation calls
- Memory-constrained environments

## Continuous Benchmarking

The benchmark suite can be integrated into CI/CD pipelines to:
- Monitor performance regressions
- Compare different SIMD implementations
- Validate optimizations across different hardware
- Track memory usage patterns

## Benchmark Results Location

Results are saved to `BenchmarkDotNet.Artifacts/results/` in multiple formats:
- `.html`: Detailed HTML report
- `.csv`: Raw data for analysis
- `.md`: GitHub-flavored markdown
- `.log`: Detailed execution log

These artifacts are excluded from version control via `.gitignore`.
