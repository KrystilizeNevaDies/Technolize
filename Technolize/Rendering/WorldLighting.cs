using System.Numerics;
using Technolize.Utils;

namespace Technolize.Rendering;

public sealed record WorldLighting
{
    public static WorldLighting Default { get; } = new();

    public Vector2 SunDirection { get; init; } = Vector2.Normalize(new Vector2(-0.35f, -1.0f));

    // Shadows are now HARD (a single SDF sphere-traced ray per light), so SunRayCount no longer affects
    // the rendered output. It is retained for the interactive control / API compatibility only; the
    // soft-shadow angular-spread averaging it used to drive has been replaced.
    public int SunRayCount { get; init; } = 9;

    // RGB lighting: the sun is tinted by SunColor (scaled by SunIntensity), AmbientColor is the
    // unbounded ambient floor, and each WorldLightSource is a coloured point light with a hard shadow.
    public Color SunColor { get; init; } = new(255, 244, 214);
    public float SunIntensity { get; init; } = 1.15f;
    public Color AmbientColor { get; init; } = new(34, 40, 54);
    public IReadOnlyList<WorldLightSource> LightSources { get; init; } = Array.Empty<WorldLightSource>();
}

public readonly record struct WorldLightSource(Vector2 Position, Color Color, float Radius, float Intensity = 1.0f);