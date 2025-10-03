using NUnit.Framework.Interfaces;
using Raylib_cs;
namespace Technolize.Test.Shader;

/// <summary>
/// NUnit test attribute that manages Raylib window lifecycle for GPU/shader tests.
/// Automatically detects headless environments and skips GPU tests when display is unavailable.
/// This prevents test crashes in CI/CD pipelines and headless containers.
/// </summary>
/// <param name="width">Window width (default: 1)</param>
/// <param name="height">Window height (default: 1)</param>
public class RaylibWindowAttribute (int width = 1, int height = 1) : Attribute, ITestAction
{
    private static bool _headlessWarningShown = false;

    public void BeforeTest(ITest test)
    {
        // Check if we're running in a headless environment
        if (IsHeadlessEnvironment())
        {
            // Display prominent warning with instructions for running GPU tests in headless mode (only once per test run)
            if (!_headlessWarningShown)
            {
                var warningMessage = BuildHeadlessWarningMessage();
                Console.WriteLine(warningMessage);
                _headlessWarningShown = true;
            }
            Assert.Ignore("Skipping GPU test - running in headless environment without display support");
            return;
        }

        if (!Raylib.IsWindowReady())
        {
            Raylib.InitWindow(width, height, "raylib test");
        }
    }

    public void AfterTest(ITest test)
    {
        if (Raylib.IsWindowReady())
        {
            Raylib.CloseWindow();
        }
    }

    public ActionTargets Targets => ActionTargets.Test;

    /// <summary>
    /// Determines if we're running in a headless environment where GPU/display tests should be skipped.
    /// This prevents GPU tests from crashing in CI environments or headless containers.
    /// </summary>
    /// <returns>True if running in headless environment, false if display is available</returns>
    private static bool IsHeadlessEnvironment()
    {
        // Check if DISPLAY environment variable is set (required for X11/Linux GUI)
        string? displayVar = Environment.GetEnvironmentVariable("DISPLAY");
        if (string.IsNullOrEmpty(displayVar))
        {
            return true;
        }

        // Check for common CI/headless environment variables
        string? ci = Environment.GetEnvironmentVariable("CI");
        string? githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        string? headless = Environment.GetEnvironmentVariable("HEADLESS");
        
        if (!string.IsNullOrEmpty(ci) || !string.IsNullOrEmpty(githubActions) || !string.IsNullOrEmpty(headless))
        {
            return true;
        }

        return false; // Assume GUI is available
    }

    /// <summary>
    /// Builds a comprehensive warning message when GPU tests are skipped in headless environments.
    /// Provides instructions on how to run these tests using virtual display technology.
    /// </summary>
    /// <returns>Formatted warning message with instructions</returns>
    private static string BuildHeadlessWarningMessage()
    {
        return """

        ╔══════════════════════════════════════════════════════════════════════════════════════╗
        ║                                  ⚠️  GPU TEST SKIPPED ⚠️                                ║
        ╠══════════════════════════════════════════════════════════════════════════════════════╣
        ║ GPU/shader tests are automatically skipped in headless environments to prevent       ║
        ║ crashes. However, you CAN run these tests using virtual display technology!          ║
        ║                                                                                      ║
        ║ 🚀 QUICK START:                                                                     ║
        ║   ./run-gpu-tests-headless.sh              (Linux/macOS)                            ║
        ║   .\run-gpu-tests-headless.ps1             (Windows)                                ║
        ║                                                                                      ║
        ║ 📖 FULL DOCUMENTATION:                                                              ║
        ║   docs/HEADLESS-GPU-TESTING.md                                                      ║
        ║                                                                                      ║
        ║ 🔧 MANUAL SETUP (Ubuntu/Debian):                                                   ║
        ║   sudo apt-get install xvfb libgl1-mesa-dri mesa-libgallium libglx-mesa0           ║
        ║   unset CI GITHUB_ACTIONS HEADLESS                                                  ║
        ║   DISPLAY=:99 xvfb-run -a dotnet test --filter "RaylibWindow"                      ║
        ║                                                                                      ║
        ║ ℹ️  This uses software rendering (Mesa llvmpipe) and works in any environment      ║
        ╚══════════════════════════════════════════════════════════════════════════════════════╝

        """;
    }
}
