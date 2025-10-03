using Technolize.Test.Validation;
using Technolize.Test.Shader;

namespace Technolize.Test.Integration;

[TestFixture]
public class WorldShaderRendererIntegrationTest
{
    [Test]
    [RaylibWindow(800, 600)]
    public void WorldShaderRenderer_IntegrationValidation_PassesAllChecks()
    {
        // Redirect console output to capture validation results
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);
        
        try
        {
            // Run the validation
            WorldShaderRendererValidation.RunValidation();
            
            // Get the output
            var output = sw.ToString();
            
            // Restore console
            Console.SetOut(originalOut);
            
            // Check that all validations passed
            Assert.That(output, Contains.Substring("✓ Successfully created both renderers"));
            Assert.That(output, Contains.Substring("✓ Basic operations test passed"));
            Assert.That(output, Contains.Substring("✓ Camera operations test passed"));
            Assert.That(output, Contains.Substring("✓ World bounds calculation test passed"));
            Assert.That(output, Contains.Substring("✓ Rendering calls test passed"));
            Assert.That(output, Contains.Substring("✓ All validation tests passed successfully!"));
            Assert.That(output, Contains.Substring("✓ WorldShaderRenderer implementation is working correctly"));
            
            // Ensure no errors occurred
            Assert.That(output, Does.Not.Contain("✗"));
            Assert.That(output, Does.Not.Contain("failed"));
            Assert.That(output, Does.Not.Contain("error"));
            
            Console.WriteLine("=== Integration Test Results ===");
            Console.WriteLine(output);
        }
        catch (Exception ex)
        {
            Console.SetOut(originalOut);
            Assert.Fail($"Integration validation failed: {ex.Message}");
        }
    }
}