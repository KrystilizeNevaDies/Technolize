using NUnit.Framework.Interfaces;
using Raylib_cs;
namespace Technolize.Test.Shader;

public class RaylibWindowAttribute (int width = 1, int height = 1) : Attribute, ITestAction
{

    public void BeforeTest(ITest test)
    {
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
}
