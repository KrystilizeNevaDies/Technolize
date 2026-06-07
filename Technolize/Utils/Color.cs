using System.Numerics;

namespace Technolize.Utils;

/// <summary>
/// An in-house RGBA8 colour value, dependency-free. This is the canonical colour type for world/block
/// data and the Silk.NET rendering path.
///
/// It exposes <see cref="R"/>, <see cref="G"/>, <see cref="B"/>, <see cref="A"/> byte channels and is a
/// value type with structural equality so it can key dictionaries (e.g. the block colour → block lookup
/// in <c>BlockRegistry</c>).
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }
    public byte A { get; }

    public Color(byte r, byte g, byte b, byte a = 255)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    /// <summary>
    /// Convenience overload for integer literals (e.g. <c>new Color(255, 196, 64, 32)</c>). Channels are
    /// clamped to the 0–255 byte range.
    /// </summary>
    public Color(int r, int g, int b, int a = 255)
        : this(ClampByte(r), ClampByte(g), ClampByte(b), ClampByte(a))
    {
    }

    public static readonly Color White = new(255, 255, 255, 255);
    public static readonly Color Black = new(0, 0, 0, 255);
    public static readonly Color Blank = new(0, 0, 0, 0);

    /// <summary>Returns the colour as a normalized <c>(r, g, b, a)</c> vector in [0, 1] (for shaders/ImGui).</summary>
    public Vector4 ToVector4() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    /// <summary>Returns the RGB channels as a normalized vector in [0, 1].</summary>
    public Vector3 ToVector3() => new(R / 255f, G / 255f, B / 255f);

    private static byte ClampByte(int value) => (byte)Math.Clamp(value, 0, 255);

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public override string ToString() => $"Color({R}, {G}, {B}, {A})";
}
