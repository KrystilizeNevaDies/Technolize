using System.Numerics;
using Raylib_cs;

namespace Technolize.Rendering;

public sealed record WorldLighting
{
    public static WorldLighting Default { get; } = new();

    public Vector2 SunDirection { get; init; } = Vector2.Normalize(new Vector2(-0.35f, -1.0f));
    public int SunRayCount { get; init; } = 9;
    public Color SunColor { get; init; } = new(255, 244, 214);
    public float SunIntensity { get; init; } = 1.15f;
    public Color AmbientColor { get; init; } = new(34, 40, 54);
    public IReadOnlyList<WorldLightSource> LightSources { get; init; } = Array.Empty<WorldLightSource>();
}

public readonly record struct WorldLightSource(Vector2 Position, Color Color, float Radius, float Intensity = 1.0f);