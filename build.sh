#!/bin/bash

# Build script for Technolize with Rust signature processor

set -e

echo "Building Rust signature processor..."
cd signature_rs
cargo build --release
cd ..

echo "Copying Rust library to main project..."
cp signature_rs/target/release/libsignature_rs.so Technolize/

echo "Building .NET project..."
dotnet build --verbosity minimal

echo "Running signature tests..."
dotnet test --filter "FullyQualifiedName~Signature" --verbosity minimal

echo "Build complete!"