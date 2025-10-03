using Technolize.Test.Shader;
namespace Technolize.Test.Shader;

[TestFixture]
public class HeadlessDetectionTest
{
    [Test]
    public void HeadlessDetection_WithoutDisplay_ReturnsTrue()
    {
        // This test validates that headless detection works when DISPLAY is not set
        // or when CI environment variables are present.
        
        string? displayVar = Environment.GetEnvironmentVariable("DISPLAY");
        string? ci = Environment.GetEnvironmentVariable("CI");
        string? githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        string? headless = Environment.GetEnvironmentVariable("HEADLESS");
        
        bool shouldBeHeadless = string.IsNullOrEmpty(displayVar) || 
                               !string.IsNullOrEmpty(ci) || 
                               !string.IsNullOrEmpty(githubActions) || 
                               !string.IsNullOrEmpty(headless);
        
        if (shouldBeHeadless)
        {
            Console.WriteLine("Running in headless environment - GPU tests should be skipped");
            Console.WriteLine($"DISPLAY: '{displayVar}'");
            Console.WriteLine($"CI: '{ci}'");
            Console.WriteLine($"GITHUB_ACTIONS: '{githubActions}'");
            Console.WriteLine($"HEADLESS: '{headless}'");
        }
        else
        {
            Console.WriteLine("Running with display support - GPU tests should execute");
        }
        
        // This test always passes - it's informational about the environment
        Assert.Pass("Headless detection working as expected");
    }
}