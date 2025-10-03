# Signature Processor Rust Integration

This directory contains a cross-platform Rust implementation of the signature computation algorithm used by Technolize.

## Overview

The signature processor computes hash-based signatures for 3x3 pixel neighborhoods, which are used for pattern recognition and world generation in Technolize. The Rust implementation provides:

- **Cross-platform compatibility**: Works on Linux, Windows, and macOS
- **Performance**: Optimized Rust implementation with efficient memory access patterns
- **Safety**: Memory-safe implementation without unsafe pointer arithmetic in the main logic
- **Interoperability**: C-compatible FFI interface for seamless integration with .NET

## Architecture

### Rust Library (`signature_rs/`)
- **`compute_signature_3x3`**: Computes signature for a single 3x3 matrix
- **`compute_signatures`**: Computes signatures for all pixels in a 2D array
- Exports as both static library (`.a`) and dynamic library (`.so`/`.dll`)

### C# Integration
- **`SignatureProcessorRust.cs`**: P/Invoke wrapper for the Rust functions
- **`SignatureProcessor.cs`**: Updated to use Rust implementation as backend
- Maintains full backward compatibility with existing API

## Building

### Prerequisites
- Rust (1.70+)
- .NET 9.0 SDK

### Build Process
```bash
# Build everything (automated)
./build.sh

# Or manually:
cd signature_rs
cargo build --release
cd ..
cp signature_rs/target/release/libsignature_rs.so Technolize/
dotnet build
```

## Algorithm Details

The signature computation uses a hash-based approach with multiple prime numbers for mixing:

1. Initialize with prime constant P1
2. XOR with seed and multiply by P2
3. For each value in 3x3 neighborhood:
   - XOR with value
   - Multiply by unique prime constant (P3-P11)

This ensures:
- High sensitivity to input changes
- Good distribution of output values
- Deterministic results for identical inputs
- Different results for different seeds

## Testing

The implementation includes comprehensive tests:

```bash
# Test Rust library
cd signature_rs && cargo test

# Test C# integration
dotnet test --filter "Signature"
```

## Performance

The Rust implementation provides several performance advantages:
- Efficient memory layout and access patterns
- LLVM optimizations
- No garbage collection overhead for computation-heavy operations
- Cross-platform native performance

## Compatibility

The Rust implementation is designed to be 100% compatible with the original C# implementation:
- Identical algorithm and constants
- Same output for same inputs
- Same API surface in C#
- All existing tests pass without modification