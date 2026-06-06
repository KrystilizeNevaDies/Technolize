#!/bin/bash

# Benchmarks live game rendering (WorldShaderRenderer) under WSL/Linux.
#
# By default it renders on WSLg - the display server WSL2 ships - which uses your real GPU through
# the Mesa d3d12 driver when the host GPU driver supports it. No Xvfb and no software rendering are
# forced; the benchmark reflects whatever GPU path WSLg provides. The renderer prints the OpenGL
# vendor/renderer/version on startup so you can see exactly what executed the shader.
#
# Modes:
#   (default)      Render on WSLg (real GPU when available).
#   --software     Force Mesa llvmpipe software rendering (deterministic CPU-only comparison).
#   --xvfb         Render on a virtual Xvfb screen (truly headless hosts with no WSLg). Implies
#                  software rendering.
#
# Usage (from inside WSL, at the repo root):
#   ./benchmark-render.sh [frameCount] [--software|--xvfb]
#
# Or from Windows PowerShell via the wrapper:
#   .\benchmark-render-wsl.ps1 -Frames 600

set -e

FRAMES=600
MODE="wslg"

for arg in "$@"; do
    case "$arg" in
        --software) MODE="software" ;;
        --xvfb)     MODE="xvfb" ;;
        ''|*[!0-9]*) ;;            # ignore non-numeric flags here
        *) FRAMES="$arg" ;;        # first bare number is the frame count
    esac
done

echo "=== Technolize Live Render Benchmark (WSL) ==="
echo "Frames: $FRAMES   Mode: $MODE"
echo

# --- Dependency checks -------------------------------------------------------
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: dotnet is not on PATH inside WSL. Install the .NET 8 SDK in your WSL distro:"
    echo "  https://learn.microsoft.com/dotnet/core/install/linux"
    exit 1
fi

# Bypass the test suite's headless auto-skip (we WANT to render here).
unset CI GITHUB_ACTIONS HEADLESS

# The project targets net8.0; allow it to run on a newer installed runtime (e.g. WSL ships .NET 10)
# instead of requiring the exact .NET 8 runtime to be present.
export DOTNET_ROLL_FORWARD=Major

# Run from the repo root (the directory containing this script).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# --- Mode setup --------------------------------------------------------------
RUN_PREFIX=()
case "$MODE" in
    wslg)
        if [ -z "$DISPLAY" ] && [ -z "$WAYLAND_DISPLAY" ]; then
            echo "WARNING: no DISPLAY/WAYLAND_DISPLAY found - WSLg may be unavailable."
            echo "         If rendering fails, retry with --xvfb (software) instead."
        fi
        : "${DISPLAY:=:0}"
        export DISPLAY
        # Prefer the GPU d3d12 driver; Mesa falls back to llvmpipe on its own if it can't engage.
        export MESA_LOADER_DRIVER_OVERRIDE="${MESA_LOADER_DRIVER_OVERRIDE:-d3d12}"
        export LD_LIBRARY_PATH="/usr/lib/wsl/lib:${LD_LIBRARY_PATH}"
        echo "Rendering on WSLg display $DISPLAY (real GPU when available)."
        ;;
    software)
        : "${DISPLAY:=:0}"
        export DISPLAY
        export LIBGL_ALWAYS_SOFTWARE=1
        export GALLIUM_DRIVER=llvmpipe
        echo "Rendering on WSLg display $DISPLAY with FORCED software GL (llvmpipe)."
        ;;
    xvfb)
        if ! command -v xvfb-run &> /dev/null; then
            echo "ERROR: --xvfb requested but xvfb is not installed. Install with:"
            echo "  sudo apt-get install -y xvfb libgl1-mesa-dri mesa-libgallium libglx-mesa0 libosmesa6"
            exit 1
        fi
        export LIBGL_ALWAYS_SOFTWARE=1
        export GALLIUM_DRIVER=llvmpipe
        RUN_PREFIX=(xvfb-run -a -s "-screen 0 ${RENDER_PROFILE_RES:-1280x720}x24")
        echo "Rendering on a virtual Xvfb screen with software GL (llvmpipe)."
        ;;
esac

echo

# --- Build -------------------------------------------------------------------
echo "Building Technolize.Test (Release)..."
dotnet build Technolize.Test/Technolize.Test.csproj -c Release --nologo -v quiet

# --- Run ---------------------------------------------------------------------
echo
echo "Running render benchmark (watch the 'OpenGL renderer' line below to confirm GPU vs software)..."
echo

"${RUN_PREFIX[@]}" dotnet run --project Technolize.Test -c Release --no-build -- profile-render "$FRAMES"

echo
echo "=== Done ==="
echo "If the OpenGL renderer above is 'llvmpipe', rendering was CPU/software (update your host GPU"
echo "driver / WSL for d3d12 GPU acceleration). A real GPU renderer name means hardware-accelerated."

