# PowerShell script to run GPU tests in headless mode on Windows
# This uses software rendering when dedicated GPU drivers are not available

param(
    [switch]$Help,
    [string]$Filter = "FullyQualifiedName~TextureRendering_DoesNotCrash_WithValidWorld|FullyQualifiedName~MinimalDeciderOutput|FullyQualifiedName~CoordShaderGradient|FullyQualifiedName~MinimalShaderOutput|FullyQualifiedName~ShaderReadsTextureValue|FullyQualifiedName~ShaderCanProcess|FullyQualifiedName~CanWriteAndReadPixelInRenderTexture|FullyQualifiedName~TestPadding"
)

if ($Help) {
    Write-Host @"
GPU Headless Test Runner for Windows

USAGE:
    .\run-gpu-tests-headless.ps1 [OPTIONS]

OPTIONS:
    -Help           Show this help message
    -Filter <str>   Custom test filter (default: all GPU tests)

EXAMPLES:
    .\run-gpu-tests-headless.ps1
    .\run-gpu-tests-headless.ps1 -Filter "MinimalShaderOutput"

REQUIREMENTS:
    - .NET 9.0+ SDK
    - Windows 10/11 with built-in software rendering support
    - PowerShell 5.1+ or PowerShell Core 7+

NOTE:
    On Windows, software OpenGL rendering is handled by the built-in
    software implementation or Mesa3D if installed. No additional
    packages are typically required.
"@
    exit 0
}

Write-Host "=== Running GPU Tests in Headless Mode (Windows) ===" -ForegroundColor Green
Write-Host "Using Windows software OpenGL rendering" -ForegroundColor Yellow
Write-Host

# Check .NET SDK
try {
    $dotnetVersion = & dotnet --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed"
    }
    Write-Host "✓ .NET SDK version: $dotnetVersion" -ForegroundColor Green
} catch {
    Write-Host "✗ ERROR: .NET SDK not found or not accessible" -ForegroundColor Red
    Write-Host "Please install .NET 9.0+ SDK from: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 1
}

# Store original environment variables
$originalCI = $env:CI
$originalGitHubActions = $env:GITHUB_ACTIONS  
$originalHeadless = $env:HEADLESS

# Temporarily unset CI environment variables that trigger headless detection
$env:CI = $null
$env:GITHUB_ACTIONS = $null
$env:HEADLESS = $null

Write-Host "Environment prepared for GPU testing:" -ForegroundColor Cyan
Write-Host "  CI variables temporarily unset" -ForegroundColor White
Write-Host "  Using Windows software OpenGL rendering" -ForegroundColor White
Write-Host

try {
    Write-Host "Starting GPU tests..." -ForegroundColor Cyan
    
    # Run GPU tests
    & dotnet test --verbosity normal --filter $Filter
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`n=== GPU Tests Completed Successfully ===" -ForegroundColor Green
        Write-Host "All GPU tests passed using software rendering" -ForegroundColor White
    } else {
        Write-Host "`n=== GPU Tests Failed ===" -ForegroundColor Red
        Write-Host "Check the output above for error details" -ForegroundColor Yellow
    }
    
} catch {
    Write-Host "`n=== GPU Tests Error ===" -ForegroundColor Red
    Write-Host "Exception: $($_.Exception.Message)" -ForegroundColor Yellow
} finally {
    # Restore original environment variables
    $env:CI = $originalCI
    $env:GITHUB_ACTIONS = $originalGitHubActions
    $env:HEADLESS = $originalHeadless
    
    Write-Host "`nEnvironment variables restored" -ForegroundColor Cyan
}

Write-Host "`nNOTE: Tests ran using Windows software OpenGL implementation" -ForegroundColor Yellow
Write-Host "For better performance, install dedicated GPU drivers if available" -ForegroundColor Yellow