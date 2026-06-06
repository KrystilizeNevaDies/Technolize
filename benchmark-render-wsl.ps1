# Launches the live-render benchmark inside WSL.
#
# By default it renders on WSLg (the display server WSL2 ships), which uses your real GPU via the
# Mesa d3d12 driver when the host GPU driver supports it - so this measures real GPU performance, not
# software rendering. The renderer prints the OpenGL renderer string so you can confirm GPU vs
# software. Use -Software to force llvmpipe (deterministic CPU comparison) or -Xvfb for truly
# headless hosts with no WSLg.
#
# Usage (from Windows, at the repo root):
#   .\benchmark-render-wsl.ps1
#   .\benchmark-render-wsl.ps1 -Frames 1200
#   .\benchmark-render-wsl.ps1 -Software

param(
    [int]$Frames = 600,
    [switch]$Software,
    [switch]$Xvfb,
    [switch]$Help
)

if ($Help) {
    Write-Host @"
Technolize live-render benchmark (via WSL)

USAGE:
    .\benchmark-render-wsl.ps1 [-Frames <n>] [-Software | -Xvfb]

OPTIONS:
    -Frames <n>   Number of measured frames (default: 600)
    -Software     Force Mesa llvmpipe software rendering (CPU-only, deterministic comparison)
    -Xvfb         Render on a virtual Xvfb screen (headless hosts with no WSLg; implies software)
    -Help         Show this help

REQUIREMENTS:
    - WSL2 with WSLg (default on recent Windows) and a Linux distro
    - Inside that distro: .NET 8 SDK
    - For real GPU acceleration: an up-to-date host GPU driver with WSL support
    - For -Xvfb only: xvfb + Mesa software GL inside the distro
"@
    exit 0
}

$ErrorActionPreference = "Stop"

if (-not (Get-Command wsl -ErrorAction SilentlyContinue)) {
    Write-Error "WSL is not available. Install it with: wsl --install"
    exit 1
}

# Resolve this script's folder (repo root) and convert it to a WSL path.
$scriptPath = Join-Path $PSScriptRoot "benchmark-render.sh"
if (-not (Test-Path $scriptPath)) {
    Write-Error "Could not find benchmark-render.sh next to this script."
    exit 1
}

# wsl.exe mangles backslashes in arguments, so hand wslpath a forward-slash Windows path.
$scriptPathForward = $scriptPath -replace '\\', '/'
$wslScriptPath = (& wsl wslpath -a "$scriptPathForward").Trim()

$mode = if ($Software) { "--software" } elseif ($Xvfb) { "--xvfb" } else { "" }

Write-Host "=== Technolize Live Render Benchmark (via WSL) ===" -ForegroundColor Green
Write-Host "Frames: $Frames   Mode: $(if ($mode) { $mode } else { 'wslg (real GPU when available)' })" -ForegroundColor Yellow
Write-Host

# Run the bash script inside WSL; pass the frame count and optional mode flag through.
if ($mode) {
    & wsl bash "$wslScriptPath" $Frames $mode
} else {
    & wsl bash "$wslScriptPath" $Frames
}
exit $LASTEXITCODE
