using Raylib_cs;
using Technolize.Utils;
namespace Technolize.Test.World;

[TestFixture]
public class PackingTest
{

    [Test]
    public void ColorPackUnpack()
    {
        for (int i = 0; i < short.MaxValue; i += Math.Max(1, i / 10))
        {
            Color packedColor = Packing.PackIntToColor(i);
            int unpackedValue = Packing.UnpackColorToInt(packedColor);
            Assert.That(unpackedValue, Is.EqualTo(i), $"Failed for value {i}");
        }
    }
}
