using System.Numerics;

namespace Technolize.Rendering.Lighting;

/// <summary>
/// Propagates a single light ray through the optical <see cref="CpuLightingField"/> and deposits the
/// light it carries into the shared <see cref="LightAccumulator"/>. This is the lighting-agnostic core
/// of the CPU lighting stage: it knows how light interacts with the world (per-cell attenuation/tint,
/// and reflection/refraction at material borders via Fresnel + Snell), but nothing about where the ray
/// came from. Every <see cref="ILightEmitter"/> generates rays and hands them here.
///
/// The march is a grid DDA (Amanatides &amp; Woo). At each cell boundary where the refraction index
/// changes, a Fresnel split spawns a reflected ray (bounded by <see cref="MaxBounces"/>) and bends the
/// transmitted ray by Snell's law. Light is deposited into every non-empty cell the ray enters and
/// attenuated by that cell's transmission, so opaque cells terminate the ray at their surface.
/// </summary>
public static class LightRayMarcher
{
    private const int MaxBounces = 4;
    private const float MinRadiance = 0.003f;
    private const float RefractionEpsilon = 1e-4f;

    // Maximum tilt (radians) applied to a material interface normal by the static surface wave. This is
    // what turns parallel light entering a refractive medium (e.g. the flat top of a lake) into
    // converging/diverging beams, so the in-water lighting reads as continuous caustic streams instead
    // of flat depth-only horizontal bands.
    private const float SurfaceWaveAmplitude = 0.22f;

    private readonly record struct RaySegment(Vector2 Pos, Vector2 Dir, Vector3 Radiance, float Medium, int Bounces, float Flux);

    /// <summary>
    /// Casts a ray of <paramref name="radiance"/> from <paramref name="origin"/> in <paramref name="direction"/>
    /// (both in field cell space), depositing light into <paramref name="accumulator"/>. <paramref name="flux"/>
    /// scales every deposit so the result is independent of the number of rays an emitter casts (a denser
    /// ray set carries proportionally less flux per ray); emitters pass the per-ray beam width here.
    /// </summary>
    public static void March(CpuLightingField field, LightAccumulator accumulator, Vector2 origin, Vector2 direction, Vector3 radiance, float flux = 1f)
    {
        if (direction.LengthSquared() < 1e-12f || MaxComponent(radiance) * flux < MinRadiance)
        {
            return;
        }

        // A small explicit stack carries reflected rays so reflections are handled iteratively (no
        // recursion) with a hard bounce cap.
        Stack<RaySegment> stack = new();
        stack.Push(new RaySegment(origin, Vector2.Normalize(direction), radiance, CpuLightingField.AirRefraction, 0, flux));

        int maxSteps = field.Width + field.Height + 8;

        while (stack.Count > 0)
        {
            RaySegment segment = stack.Pop();
            MarchSegment(field, accumulator, segment, stack, maxSteps);
        }
    }

    private static void MarchSegment(
        CpuLightingField field, LightAccumulator accumulator, RaySegment segment, Stack<RaySegment> stack, int maxSteps)
    {
        Vector2 pos = segment.Pos;
        Vector2 dir = segment.Dir;
        Vector3 radiance = segment.Radiance;
        float currentRefraction = segment.Medium;
        int bounces = segment.Bounces;
        float flux = segment.Flux;

        int cellX = (int)MathF.Floor(pos.X);
        int cellY = (int)MathF.Floor(pos.Y);

        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);

        float tDeltaX = dir.X != 0 ? MathF.Abs(1f / dir.X) : float.PositiveInfinity;
        float tDeltaY = dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.PositiveInfinity;

        float tMaxX = dir.X > 0
            ? (cellX + 1 - pos.X) / dir.X
            : (dir.X < 0 ? (cellX - pos.X) / dir.X : float.PositiveInfinity);
        float tMaxY = dir.Y > 0
            ? (cellY + 1 - pos.Y) / dir.Y
            : (dir.Y < 0 ? (cellY - pos.Y) / dir.Y : float.PositiveInfinity);

        bool entered = false;

        for (int step = 0; step < maxSteps; step++)
        {
            // Step to the next cell, recording which axis we crossed (the interface normal axis) and the
            // ray parameter t at the crossing (for the boundary point of any reflection).
            float tCross;
            Vector2 normal;
            if (tMaxX < tMaxY)
            {
                cellX += stepX;
                tCross = tMaxX;
                tMaxX += tDeltaX;
                normal = new Vector2(-stepX, 0f);
            }
            else
            {
                cellY += stepY;
                tCross = tMaxY;
                tMaxY += tDeltaY;
                normal = new Vector2(0f, -stepY);
            }

            bool inBounds = field.InBounds(cellX, cellY);
            if (!inBounds)
            {
                // Left the field after having travelled through it: the ray escapes.
                if (entered)
                {
                    return;
                }

                continue;
            }

            entered = true;

            float nextRefraction = field.RefractionAt(cellX, cellY);

            // Material border: split into a reflected ray and a refracted (transmitted) ray.
            if (MathF.Abs(nextRefraction - currentRefraction) > RefractionEpsilon)
            {
                // Perturb the axis-aligned interface normal with a static surface wave so parallel rays
                // entering the medium fan out into converging/diverging beams (caustics). Without this,
                // a flat surface refracts every ray identically and the in-medium light becomes a uniform
                // depth-only gradient (the horizontal banding); the wave breaks it into light streams.
                Vector2 boundaryPoint = pos + dir * tCross;
                Vector2 surfaceNormal = PerturbNormal(normal, boundaryPoint, field.Resolution);

                float cosIncident = MathF.Abs(Vector2.Dot(dir, surfaceNormal));
                float reflectance = FresnelSchlick(cosIncident, currentRefraction, nextRefraction);

                Vector3 reflectedRadiance = radiance * reflectance;
                if (bounces < MaxBounces && MaxComponent(reflectedRadiance) * flux >= MinRadiance)
                {
                    Vector2 reflectedDir = Reflect(dir, surfaceNormal);
                    stack.Push(new RaySegment(boundaryPoint, reflectedDir, reflectedRadiance, currentRefraction, bounces + 1, flux));
                }

                radiance *= 1f - reflectance;

                if (TryRefract(dir, surfaceNormal, currentRefraction, nextRefraction, out Vector2 refractedDir))
                {
                    dir = refractedDir;
                }
                else
                {
                    // Total internal reflection: all energy went to the reflected ray.
                    return;
                }
            }

            // Deposit the arriving light into the surface/volume cell (empty cells let light pass).
            if (!field.IsEmpty(cellX, cellY))
            {
                accumulator.Deposit(cellX, cellY, radiance * flux);
            }

            // Attenuate (and tint) by travelling through this cell; opaque cells drop radiance to zero.
            radiance *= field.TransmissionAt(cellX, cellY);
            currentRefraction = nextRefraction;

            if (MaxComponent(radiance) * flux < MinRadiance)
            {
                return;
            }
        }
    }

    /// <summary>Schlick's approximation of the Fresnel reflectance between two media.</summary>
    private static float FresnelSchlick(float cosIncident, float n1, float n2)
    {
        float r0 = (n1 - n2) / (n1 + n2);
        r0 *= r0;

        // When passing into a denser medium the cosine to use is the incident one; for the rarer
        // direction Snell can yield total internal reflection, handled by TryRefract.
        float x = 1f - cosIncident;
        return r0 + (1f - r0) * x * x * x * x * x;
    }

    private static Vector2 Reflect(Vector2 dir, Vector2 normal) => dir - 2f * Vector2.Dot(dir, normal) * normal;

    /// <summary>
    /// Tilts an axis-aligned interface <paramref name="normal"/> by a small angle driven by a static
    /// multi-octave wave sampled along the surface tangent at <paramref name="point"/>. This stands in
    /// for surface roughness/ripples: neighbouring rays crossing the interface get slightly different
    /// normals, so they refract into converging and diverging beams that read as caustic light streams.
    /// The tangent coordinate is divided by <paramref name="resolution"/> so the wave is measured in
    /// world tiles, keeping the stream spacing identical regardless of the lighting resolution multiplier.
    /// </summary>
    private static Vector2 PerturbNormal(Vector2 normal, Vector2 point, int resolution)
    {
        Vector2 tangent = new(-normal.Y, normal.X);
        float t = Vector2.Dot(point, tangent) / resolution;
        float angle = SurfaceWaveAmplitude * SurfaceWave(t);
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vector2(normal.X * cos - normal.Y * sin, normal.X * sin + normal.Y * cos);
    }

    /// <summary>Sum of incommensurate sines (range ~[-1, 1]) giving a smooth, non-repeating surface ripple.</summary>
    private static float SurfaceWave(float t)
    {
        return 0.6f * MathF.Sin(t * 0.21f)
             + 0.3f * MathF.Sin(t * 0.57f + 1.7f)
             + 0.1f * MathF.Sin(t * 1.30f + 4.2f);
    }

    /// <summary>
    /// Snell refraction across an interface whose <paramref name="normal"/> faces back toward the
    /// incoming medium. Returns false on total internal reflection.
    /// </summary>
    private static bool TryRefract(Vector2 dir, Vector2 normal, float n1, float n2, out Vector2 refracted)
    {
        float eta = n1 / n2;
        float cosI = -Vector2.Dot(dir, normal);
        float sin2T = eta * eta * (1f - cosI * cosI);

        if (sin2T > 1f)
        {
            refracted = Vector2.Zero;
            return false;
        }

        float cosT = MathF.Sqrt(1f - sin2T);
        refracted = Vector2.Normalize(eta * dir + (eta * cosI - cosT) * normal);
        return true;
    }

    private static float MaxComponent(Vector3 v) => MathF.Max(v.X, MathF.Max(v.Y, v.Z));
}
