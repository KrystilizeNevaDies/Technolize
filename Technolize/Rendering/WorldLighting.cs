using System.Numerics;
using Technolize.Utils;

namespace Technolize.Rendering;

public sealed record WorldLighting
{
    public static WorldLighting Default { get; } = new();

    public Vector2 SunDirection { get; init; } = Vector2.Normalize(new Vector2(-0.35f, -1.0f));

    // The sun is sampled as N angularly-spread rays averaged for a soft shadow. Sparse temporal refresh
    // (WorldShaderRenderer.PixelUpdateFraction) amortizes this full-quality cost across frames, so the
    // ray count is kept at the full-quality 9 rather than reduced.
    public int SunRayCount { get; init; } = 9;
    public Color SunColor { get; init; } = new(255, 244, 214);
    public float SunIntensity { get; init; } = 1.15f;
    public Color AmbientColor { get; init; } = new(34, 40, 54);
    public IReadOnlyList<WorldLightSource> LightSources { get; init; } = Array.Empty<WorldLightSource>();
}

public readonly record struct WorldLightSource(Vector2 Position, Color Color, float Radius, float Intensity = 1.0f);