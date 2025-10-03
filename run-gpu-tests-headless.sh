#!/bin/bash

# Script to run GPU tests in headless mode using Xvfb virtual display
# This allows testing GPU/shader functionality in CI/CD pipelines and headless containers

set -e

echo "=== Running GPU Tests in Headless Mode ==="
echo "Using Xvfb (X Virtual Framebuffer) for software rendering"
echo

# Check if required packages are installed
if ! command -v xvfb-run &> /dev/null; then
    echo "ERROR: xvfb is not installed. Install with:"
    echo "  sudo apt-get install xvfb"
    exit 1
fi

# Check for Mesa drivers
if ! dpkg -l | grep -q "libgl1-mesa-dri"; then
    echo "WARNING: Mesa drivers may not be installed. Install with:"
    echo "  sudo apt-get install libgl1-mesa-dri mesa-libgallium libglx-mesa0"
fi

# Temporarily unset CI environment variables that trigger headless detection
export ORIGINAL_CI="$CI"
export ORIGINAL_GITHUB_ACTIONS="$GITHUB_ACTIONS"
export ORIGINAL_HEADLESS="$HEADLESS"

unset CI
unset GITHUB_ACTIONS  
unset HEADLESS

echo "Environment prepared for GPU testing:"
echo "  DISPLAY will be set to virtual display"
echo "  CI variables temporarily unset"
echo

# Run GPU tests with Xvfb
echo "Starting Xvfb and running GPU tests..."
echo "Running all GPU/shader tests that use RaylibWindow attribute..."

# Get list of GPU tests and run them
GPU_TESTS="TextureRendering_DoesNotCrash_WithValidWorld|MinimalDeciderOutput|CoordShaderGradient|MinimalShaderOutput|ShaderReadsTextureValue|ShaderCanProcess_1x1_Texture|ShaderCanProcess_NxN_Texture|CanWriteAndReadPixelInRenderTexture|TestPadding"

DISPLAY=:99 xvfb-run -a -s "-screen 0 1024x768x24" dotnet test --verbosity normal --filter "FullyQualifiedName~TextureRendering_DoesNotCrash_WithValidWorld|FullyQualifiedName~MinimalDeciderOutput|FullyQualifiedName~CoordShaderGradient|FullyQualifiedName~MinimalShaderOutput|FullyQualifiedName~ShaderReadsTextureValue|FullyQualifiedName~ShaderCanProcess|FullyQualifiedName~CanWriteAndReadPixelInRenderTexture|FullyQualifiedName~TestPadding"

# Restore original environment variables
export CI="$ORIGINAL_CI"
export GITHUB_ACTIONS="$ORIGINAL_GITHUB_ACTIONS"
export HEADLESS="$ORIGINAL_HEADLESS"

echo
echo "=== GPU Tests Completed ==="
echo "Note: Tests ran using Mesa llvmpipe software renderer"
echo "For better performance on real hardware, use dedicated GPU drivers"