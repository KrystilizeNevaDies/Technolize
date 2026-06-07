using System.Numerics;
using Technolize.Utils;

namespace Technolize.Test.Util;

/// <summary>
/// Unit tests for the in-house <see cref="Color"/> value type used by the world-data and Silk rendering
/// layers. They pin the construction, channel access, value equality (needed for the block-colour
/// dictionary in <c>BlockRegistry</c>), and vector conversions.
/// </summary>
[TestFixture]
public class ColorTest
{
    [Test]
    public void IntConstructor_StoresChannels_WithDefaultOpaqueAlpha()
    {
        Color color = new(25, 40, 55);

        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(25));
            Assert.That(color.G, Is.EqualTo(40));
            Assert.That(color.B, Is.EqualTo(55));
            Assert.That(color.A, Is.EqualTo(255), "alpha defaults to opaque");
        });
    }

    [Test]
    public void IntConstructor_ClampsOutOfRangeChannels()
    {
        Color color = new(-10, 300, 128, 999);

        Assert.Multiple(() =>
        {
            Assert.That(color.R, Is.EqualTo(0));
            Assert.That(color.G, Is.EqualTo(255));
            Assert.That(color.B, Is.EqualTo(128));
            Assert.That(color.A, Is.EqualTo(255));
        });
    }

    [Test]
    public void Equality_IsStructural()
    {
        Color a = new(10, 20, 30, 40);
        Color b = new(10, 20, 30, 40);
        Color c = new(10, 20, 30, 41);

        Assert.Multiple(() =>
        {
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        });
    }

    [Test]
    public void UsableAsDictionaryKey()
    {
        // BlockRegistry keys a Dictionary<Color, BlockInfo>; equal colours must collide as keys.
        Dictionary<Color, string> map = new()
        {
            [new Color(1, 2, 3)] = "first",
        };

        map[new Color(1, 2, 3)] = "second";

        Assert.Multiple(() =>
        {
            Assert.That(map, Has.Count.EqualTo(1));
            Assert.That(map[new Color(1, 2, 3)], Is.EqualTo("second"));
        });
    }

    [Test]
    public void ToVector4_NormalizesChannels()
    {
        Color color = new(255, 128, 0, 64);
        Vector4 v = color.ToVector4();

        Assert.Multiple(() =>
        {
            Assert.That(v.X, Is.EqualTo(1f).Within(1e-4));
            Assert.That(v.Y, Is.EqualTo(128f / 255f).Within(1e-4));
            Assert.That(v.Z, Is.EqualTo(0f).Within(1e-4));
            Assert.That(v.W, Is.EqualTo(64f / 255f).Within(1e-4));
        });
    }

    [Test]
    public void StaticColors_HaveExpectedChannels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Color.White, Is.EqualTo(new Color(255, 255, 255, 255)));
            Assert.That(Color.Black, Is.EqualTo(new Color(0, 0, 0, 255)));
            Assert.That(Color.Blank, Is.EqualTo(new Color(0, 0, 0, 0)));
        });
    }
}
