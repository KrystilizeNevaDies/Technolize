# Running GPU Tests in Headless Mode

This document provides detailed instructions for running GPU/shader tests in headless environments such as CI/CD pipelines, containers, and servers without displays.

## Overview

By default, the Technolize test suite automatically detects headless environments and skips GPU tests to prevent crashes. However, you can run these tests in headless mode using virtual display technology and software rendering.

## Quick Start

### Using the Provided Script

The easiest way to run GPU tests in headless mode:

```bash
./run-gpu-tests-headless.sh
```

This script handles all the setup automatically and runs all GPU tests using Xvfb virtual display.

### Manual Execution

If you prefer manual control:

```bash
# Temporarily unset CI environment variables
unset CI GITHUB_ACTIONS HEADLESS

# Run tests with Xvfb virtual display
DISPLAY=:99 xvfb-run -a -s "-screen 0 1024x768x24" dotnet test --verbosity normal --filter "FullyQualifiedName~TextureRendering_DoesNotCrash_WithValidWorld|FullyQualifiedName~MinimalDeciderOutput|FullyQualifiedName~CoordShaderGradient|FullyQualifiedName~MinimalShaderOutput|FullyQualifiedName~ShaderReadsTextureValue|FullyQualifiedName~ShaderCanProcess|FullyQualifiedName~CanWriteAndReadPixelInRenderTexture|FullyQualifiedName~TestPadding"
```

## Requirements

### System Dependencies

The following packages must be installed on your system:

#### Ubuntu/Debian:
```bash
sudo apt-get update
sudo apt-get install -y \
    xvfb \
    libgl1-mesa-dri \
    mesa-libgallium \
    libglx-mesa0 \
    libosmesa6-dev
```

#### CentOS/RHEL/Fedora:
```bash
sudo yum install -y \
    xorg-x11-server-Xvfb \
    mesa-dri-drivers \
    mesa-libGL \
    mesa-libOSMesa-devel
```

#### Alpine Linux:
```bash
apk add --no-cache \
    xvfb \
    mesa-dri-gallium \
    mesa-gl \
    mesa-osmesa
```

### .NET Dependencies

Ensure you have the .NET SDK installed:
```bash
# Check .NET version
dotnet --version

# Should be .NET 9.0 or later
```

## How It Works

### 1. Virtual Display (Xvfb)

**Xvfb (X Virtual Framebuffer)** creates a virtual X11 display that runs entirely in memory without requiring a physical display device. This allows GUI applications to run in headless environments.

Key features:
- Creates virtual displays of any resolution
- Supports multiple color depths
- No physical hardware required
- Widely supported in CI/CD environments

### 2. Software Rendering (Mesa)

**Mesa** provides software-based OpenGL rendering using the **llvmpipe** driver. This enables GPU-like functionality without dedicated graphics hardware.

Capabilities:
- OpenGL 4.5 Core Profile support
- GLSL 4.50 shader compilation
- Software-based compute shaders
- Full texture and framebuffer operations

### 3. Environment Variable Override

The test suite detects headless environments by checking:
- `DISPLAY` environment variable (empty = headless)
- `CI`, `GITHUB_ACTIONS`, `HEADLESS` variables (present = headless)

To run GPU tests, we temporarily unset these variables to bypass the headless detection.

## Supported Test Types

The following GPU/shader tests can run in headless mode:

- **Shader Compilation Tests**: Vertex and fragment shader compilation
- **Texture Processing Tests**: Texture loading, manipulation, and reading
- **Compute Shader Tests**: GPU compute operations
- **Framebuffer Tests**: Render-to-texture operations
- **World Rendering Tests**: 3D scene rendering validation

### Example Test Output

```
INFO: GL: OpenGL device information:
INFO:     > Vendor:   Mesa
INFO:     > Renderer: llvmpipe (LLVM 20.1.2, 256 bits)
INFO:     > Version:  4.5 (Core Profile) Mesa 25.0.7-0ubuntu0.24.04.2
INFO:     > GLSL:     4.50

Test summary: total: 11, failed: 0, succeeded: 11, skipped: 0
```

## Performance Considerations

### Software vs Hardware Rendering

| Aspect | Software (Mesa llvmpipe) | Hardware (GPU) |
|--------|-------------------------|----------------|
| **Speed** | Slower (~10-100x) | Native GPU speed |
| **Compatibility** | Excellent | Driver dependent |
| **Setup** | Simple | Complex driver setup |
| **CI/CD** | Perfect | Limited availability |

### Resource Usage

Software rendering uses CPU resources:
- **Memory**: ~50-200MB additional RAM
- **CPU**: Intensive for complex shaders
- **Time**: Tests may take 2-10x longer

## CI/CD Integration

### GitHub Actions

```yaml
name: GPU Tests
on: [push, pull_request]

jobs:
  gpu-tests:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'
          
      - name: Install GPU dependencies
        run: |
          sudo apt-get update
          sudo apt-get install -y xvfb libgl1-mesa-dri mesa-libgallium libglx-mesa0
          
      - name: Run GPU tests
        run: ./run-gpu-tests-headless.sh
```

### Docker

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0

# Install GPU testing dependencies
RUN apt-get update && apt-get install -y \
    xvfb \
    libgl1-mesa-dri \
    mesa-libgallium \
    libglx-mesa0 \
    && rm -rf /var/lib/apt/lists/*

COPY . /app
WORKDIR /app

# Run GPU tests
CMD ["./run-gpu-tests-headless.sh"]
```

### Jenkins

```groovy
pipeline {
    agent any
    
    stages {
        stage('Setup') {
            steps {
                sh 'sudo apt-get update'
                sh 'sudo apt-get install -y xvfb libgl1-mesa-dri mesa-libgallium libglx-mesa0'
            }
        }
        
        stage('GPU Tests') {
            steps {
                sh './run-gpu-tests-headless.sh'
            }
        }
    }
}
```

## Troubleshooting

### Common Issues

#### 1. "Unable to load shared library" errors
```bash
# Install missing Mesa drivers
sudo apt-get install libgl1-mesa-dri mesa-libgallium libglx-mesa0
```

#### 2. "Cannot open display" errors
```bash
# Ensure DISPLAY is set correctly
echo $DISPLAY
# Should show something like ":99"

# Check if Xvfb is running
ps aux | grep Xvfb
```

#### 3. Shader compilation failures
```bash
# Check OpenGL version
glxinfo | grep "OpenGL version"
# Should show OpenGL 4.5 or higher

# Verify Mesa installation
glxinfo | grep "renderer"
# Should show "llvmpipe"
```

#### 4. Tests still being skipped
```bash
# Verify CI variables are unset
echo "CI=$CI GITHUB_ACTIONS=$GITHUB_ACTIONS HEADLESS=$HEADLESS"
# All should be empty

# Check DISPLAY variable
echo "DISPLAY=$DISPLAY"
# Should not be empty
```

### Debug Mode

For detailed debugging, add these environment variables:

```bash
export RAYLIB_LOG_LEVEL=DEBUG
export MESA_DEBUG=1
export LIBGL_DEBUG=verbose

# Run tests with maximum verbosity
./run-gpu-tests-headless.sh
```

### Performance Tuning

To improve software rendering performance:

```bash
# Use all available CPU cores
export MESA_GLTHREAD=true

# Optimize for software rendering
export GALLIUM_DRIVER=llvmpipe
export LP_NUM_THREADS=$(nproc)

# Run tests
./run-gpu-tests-headless.sh
```

## Alternative Approaches

### 1. Hardware GPU with Headless Drivers

For better performance on cloud instances with GPUs:

```bash
# Install NVIDIA headless drivers (cloud instances)
sudo apt-get install nvidia-headless-450 nvidia-utils-450

# Create virtual display
sudo nvidia-xconfig --virtual=1920x1080

# Start X server
sudo X :0 &
export DISPLAY=:0

# Run tests
dotnet test --filter "RaylibWindow"
```

### 2. VNC + Hardware Acceleration

For development environments:

```bash
# Install VNC server
sudo apt-get install tightvncserver

# Start VNC session
vncserver :1 -geometry 1024x768 -depth 24

# Export display
export DISPLAY=:1

# Run tests
dotnet test --filter "RaylibWindow"
```

### 3. Container with GPU Passthrough

For Docker with GPU access:

```yaml
version: '3.8'
services:
  gpu-tests:
    build: .
    runtime: nvidia
    environment:
      - NVIDIA_VISIBLE_DEVICES=all
      - DISPLAY=:0
    volumes:
      - /tmp/.X11-unix:/tmp/.X11-unix:rw
```

## Best Practices

1. **Always test both headless and hardware modes** in your CI pipeline
2. **Use software rendering for functional tests**, hardware for performance tests
3. **Monitor resource usage** - software rendering is CPU intensive
4. **Cache dependencies** in CI to reduce setup time
5. **Run GPU tests in parallel** with CPU tests when possible
6. **Set appropriate timeouts** - software rendering takes longer

## Conclusion

Running GPU tests in headless mode enables comprehensive testing of graphics functionality in any environment. While software rendering has performance limitations, it provides 100% compatibility and allows full validation of shader logic, texture operations, and rendering pipelines without physical GPU hardware.

For most use cases, the provided `run-gpu-tests-headless.sh` script will handle all the complexity automatically, making GPU testing as simple as running any other test suite.