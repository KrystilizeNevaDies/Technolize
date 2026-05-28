using Raylib_cs;
using Technolize.Utils;
namespace Technolize.Test.Util;

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

    [TestCase(0u)]
    [TestCase(255u)]
    [TestCase(65535u)]
    [TestCase(16777215u)]
    [TestCase(2000000000u)]
    [TestCase(uint.MaxValue)]
    public void ColorPackUnpackSupportsFullUInt32(uint value)
    {
        Color packedColor = Packing.PackUIntToColor(value);
        uint unpackedValue = Packing.UnpackColorToUInt(packedColor);

        Assert.That(unpackedValue, Is.EqualTo(value));
    }
}
