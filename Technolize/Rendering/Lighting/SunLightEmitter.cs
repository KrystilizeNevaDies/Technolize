using System.Numerics;

namespace Technolize.Rendering.Lighting;

/// <summary>
/// A directional sun light for the CPU lighting stage. It emits <see cref="RayCount"/> parallel rays
/// across the field from the sun's direction; each ray marches through the world (via
/// <see cref="LightRayMarcher"/>), depositing the sun's colour into the tiles it hits and attenuating /
/// reflecting / refracting at material borders. Because all emission goes through the shared marcher and
/// accumulator, the sun is just one <see cref="ILightEmitter"/> among future light sources.
/// </summary>
public sealed class SunLightEmitter : ILightEmitter
{
    private readonly Vector2 _worldDirection;
    private readonly Vector3 _radiance;
    private readonly int _rayCount;

    /// <param name="worldDirection">Sun travel direction in world space (world Y is up).</param>
    /// <param name="radiance">Sun colour already scaled by intensity (linear, may exceed 1).</param>
    /// <param name="rayCount">Number of parallel rays cast across the field.</param>
    public SunLightEmitter(Vector2 worldDirection, Vector3 radiance, int rayCount)
    {
        _worldDirection = worldDirection;
        _radiance = radiance;
        _rayCount = Math.Max(1, rayCount);
    }

    public int RayCount => _rayCount;

    public void Emit(CpuLightingField field, LightAccumulator accumulator)
    {
        // World Y is up, but the field's Y runs top-to-bottom (row 0 is the highest world row), so the
        // vertical component is flipped into local cell space.
        Vector2 local = new(_worldDirection.X, -_worldDirection.Y);
        if (local.LengthSquared() < 1e-12f)
        {
            return;
        }

        Vector2 dir = Vector2.Normalize(local);
        Vector2 perp = new(-dir.Y, dir.X);
        Vector2 center = new(field.Width * 0.5f, field.Height * 0.5f);

        // Project the field corners onto the ray axis (dir) and the perpendicular axis (perp) to find
        // the upstream start plane and the perpendicular extent the rays must span.
        float dMin = float.MaxValue;
        float pMin = float.MaxValue;
        float pMax = float.MinValue;
        ReadOnlySpan<Vector2> corners = stackalloc Vector2[]
        {
            new Vector2(0, 0) - center,
            new Vector2(field.Width, 0) - center,
            new Vector2(0, field.Height) - center,
            new Vector2(field.Width, field.Height) - center,
        };

        foreach (Vector2 corner in corners)
        {
            float d = Vector2.Dot(corner, dir);
            float p = Vector2.Dot(corner, perp);
            dMin = MathF.Min(dMin, d);
            pMin = MathF.Min(pMin, p);
            pMax = MathF.Max(pMax, p);
        }

        // Start one cell upstream of the field so the first interface is the air -> surface boundary.
        Vector2 startBase = center + dir * (dMin - 1f);

        // Per-ray beam width: adjacent rays are this far apart along the perpendicular axis, so each ray
        // represents a slab of that width. Weighting every deposit by the slab's horizontal footprint
        // (spacing * |dir.Y|) makes a fully-lit open cell receive ~the sun's radiance regardless of the
        // ray count — more rays simply resolve the caustics more finely instead of brightening the scene.
        float spacing = (pMax - pMin) / _rayCount;
        float flux = spacing * MathF.Abs(dir.Y);

        for (int i = 0; i < _rayCount; i++)
        {
            float t = (i + 0.5f) / _rayCount;
            float perpCoord = pMin + (pMax - pMin) * t;
            Vector2 origin = startBase + perp * perpCoord;
            LightRayMarcher.March(field, accumulator, origin, dir, _radiance, flux);
        }
    }
}
