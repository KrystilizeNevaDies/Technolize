# build.ps1
# Build script for Technolize with Rust signature processor

# Equivalent of 'set -e' in bash. Stops the script on the first error.
$ErrorActionPreference = "Stop"

Write-Host "Building Rust signature processor..."
Set-Location signature_rs  # 'cd' is an alias for Set-Location
cargo build --release
Set-Location ..

Write-Host "Copying Rust library to main project..."
# 'cp' and 'copy' are aliases for Copy-Item
Copy-Item -Path "signature_rs/target/release/signature_rs.dll" -Destination "Technolize/"

Write-Host "Building .NET project..."
dotnet build --verbosity minimal

Write-Host "Running signature tests..."
dotnet test --filter "FullyQualifiedName~Signature" --verbosity minimal

Write-Host "Build complete!"
