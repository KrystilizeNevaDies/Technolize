# GPU Test Auto-Skipping in Headless Environments

## Overview

The `RaylibWindowAttribute` in this project automatically detects headless environments and skips GPU/shader tests that require display support. This prevents test failures in CI/CD pipelines and headless containers.

## How it works

The attribute checks for headless environment indicators:

1. **DISPLAY environment variable**: Empty or missing (Linux/X11)
2. **CI environment variables**: `CI`, `GITHUB_ACTIONS`, `HEADLESS`

When any of these conditions are met, GPU tests are gracefully skipped using `Assert.Ignore()` instead of attempting to initialize Raylib, which would crash.

## Test Results

- **Non-GPU tests**: Continue to run normally (32 tests pass)
- **GPU tests**: Automatically skipped with clear message (11 tests skipped)
- **Zero test failures**: No crashes or errors in headless mode

## Environment Detection

The following environment variables trigger headless mode:
- `DISPLAY` (empty/missing)
- `CI=true`
- `GITHUB_ACTIONS=true` 
- `HEADLESS=true`

## Usage

Simply apply the `[RaylibWindow]` attribute to any test that requires GPU/display:

```csharp
[Test]
[RaylibWindow]
public void MyGpuTest()
{
    // This test will run normally with display
    // or be skipped gracefully in headless mode
}
```

No additional configuration is required - the detection is automatic.

## Running GPU Tests in Headless Mode

While GPU tests are automatically skipped in headless environments for safety, you can run them using virtual display technology. See [HEADLESS-GPU-TESTING.md](HEADLESS-GPU-TESTING.md) for detailed instructions.

**Quick start:**
```bash
# Linux/macOS
./run-gpu-tests-headless.sh

# Windows  
.\run-gpu-tests-headless.ps1
```