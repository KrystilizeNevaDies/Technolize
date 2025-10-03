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

    public void BeforeTest(ITest test)
    {
        // Check if we're running in a headless environment
        if (IsHeadlessEnvironment())
        {
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
}
