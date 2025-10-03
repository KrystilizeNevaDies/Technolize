# Technolize SIMD Performance Benchmarks

This document describes the integrated benchmarking framework for Technolize's SIMD signature processing implementation.

## Overview

We've integrated [BenchmarkDotNet](https://benchmarkdotnet.org/) to provide comprehensive performance analysis of the SIMD-optimized signature computation in Technolize. The benchmarks focus on the core `SignatureProcessor` class and the `SignatureWorldTicker` that uses it.

## Running Benchmarks

### Prerequisites
- .NET 9.0 SDK
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

### 4. World Rendering Benchmarks (`WorldRendererBenchmarks`)
Tests the world rendering system performance:
- **SmallWorld_SingleFrame**: Single frame on 1x1 regions (32x32 blocks)
- **MediumWorld_SingleFrame**: Single frame on 2x2 regions (64x64 blocks)
- **LargeWorld_SingleFrame**: Single frame on 4x4 regions (128x128 blocks)
- **ActiveRegions_Rendering**: Direct block rendering for active regions
- **InactiveRegions_Rendering**: Texture caching for inactive regions
- **Camera_Update**: Camera movement and zoom operations
- **WorldBounds_Calculation**: Visible world bounds computation
- **MultipleFrames_SmallWorld**: Consecutive frame rendering stress test
- **MultipleFrames_MediumWorld**: Multi-frame test on medium world
- **HighDensity_BlockRendering**: Dense block patterns (performance stress)
- **SparseBlocks_Rendering**: Sparse block patterns (minimal rendering)
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

## Key Metrics

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