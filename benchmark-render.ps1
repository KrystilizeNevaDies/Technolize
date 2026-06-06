# Runs the live render benchmark natively on Windows, opening a real Raylib window on your GPU.
#
# It builds and launches the WorldShaderRenderer against a fixed water-heavy scene for a set number
# of frames and prints per-frame timing (Mean/Min/P50/P95/P99/Max ms + MeanFps). Because it runs
# natively, it uses your actual GPU and the real OpenGL driver - the renderer logs the GL
# vendor/renderer/version on startup so you can confirm.
#
# Usage (from the repo root):
#   .\benchmark-render.ps1
#   .\benchmark-render.ps1 -Frames 1200
#   .\benchmark-render.ps1 -Frames 600 -NoBuild

param(
    [int]$Frames = 600,
    [switch]$NoBuild,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
Technolize live-render benchmark (native Windows)

USAGE:
    .\benchmark-render.ps1 [-Frames <n>] [-NoBuild]

OPTIONS:
    -Frames <n>   Number of measured frames (default: 600)
    -NoBuild      Skip the build step (use the existing Release binaries)
    -Help         Show this help

NOTES:
    - Opens a real 1280x720 Raylib window rendered on your GPU.
    - Watch the '> Renderer:' line in the output to see which GPU/driver executed the shader.
"@
    exit 0
}

$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Error "dotnet is not on PATH. Install the .NET 8 SDK: https://aka.ms/dotnet-download"
        exit 1
    }

    Write-Host "=== Technolize Live Render Benchmark (native Windows) ===" -ForegroundColor Green
    Write-Host "Frames: $Frames" -ForegroundColor Yellow
    Write-Host

    if (-not $NoBuild) {
        Write-Host "Building Technolize.Test (Release)..."
        dotnet build Technolize.Test/Technolize.Test.csproj -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host
    }

    Write-Host "Opening render window (watch the '> Renderer:' line to confirm the GPU)..."
    Write-Host

    $buildArg = if ($NoBuild) { "--no-build" } else { "" }
    if ($buildArg) {
        dotnet run --project Technolize.Test -c Release $buildArg -- profile-render $Frames
    } else {
        dotnet run --project Technolize.Test -c Release -- profile-render $Frames
    }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
