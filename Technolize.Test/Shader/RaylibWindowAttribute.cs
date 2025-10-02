using NUnit.Framework.Interfaces;
using Raylib_cs;
namespace Technolize.Test.Shader;

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
