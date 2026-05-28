# build.ps1
# Build script for Technolize with Rust signature processor

# Equivalent of 'set -e' in bash. Stops the script on the first error.
$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Description,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Command
    )

    & $Command

    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-DotNetSdkSupport {
    $dotnetVersion = & dotnet --version

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to determine the installed .NET SDK version."
    }

    $dotnetMajorVersion = [int]($dotnetVersion -split '\.')[0]

    if ($dotnetMajorVersion -lt 8) {
        throw "The installed .NET SDK ($dotnetVersion) does not support this repository's net8.0 target. Install the .NET 8 SDK and try again."
    }
}

Write-Host "Building Rust signature processor..."
Set-Location signature_rs  # 'cd' is an alias for Set-Location
Invoke-NativeCommand "Rust build" { cargo build --release }
Set-Location ..

Write-Host "Copying Rust library to main project..."
# 'cp' and 'copy' are aliases for Copy-Item
Copy-Item -Path "signature_rs\target\release\signature_rs.dll" -Destination "Technolize\"

Write-Host "Building .NET project..."
Assert-DotNetSdkSupport
Invoke-NativeCommand ".NET build" { dotnet build --tl:off --verbosity minimal }

Write-Host "Running signature tests..."
Invoke-NativeCommand ".NET signature tests" { dotnet test --tl:off --filter "FullyQualifiedName~Signature" --verbosity minimal }

Write-Host "Build complete!"
