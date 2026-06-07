using System.Numerics;

namespace Technolize.Rendering.Lighting;

/// <summary>
/// The CPU lighting compute stage run before every frame of the colour renderer. It clears the shared
/// <see cref="LightAccumulator"/> to the ambient floor, runs each <see cref="ILightEmitter"/> (which
/// cast their rays through the world via <see cref="LightRayMarcher"/> and deposit light), and packs the
/// result into an RGBA8 lighting texture buffer for upload. The accumulator is reused frame-to-frame to
/// avoid per-frame allocations.
/// </summary>
public sealed class CpuLightingStage
{
    private LightAccumulator? _accumulator;

    /// <summary>
    /// Computes the lighting buffer for <paramref name="field"/>: ambient floor + every emitter's
    /// deposited light, clamped and packed RGBA8 (row-major, top-to-bottom, matching the field).
    /// </summary>
    public byte[] Compute(CpuLightingField field, Vector3 ambient, IReadOnlyList<ILightEmitter> emitters, out int width, out int height)
    {
        width = field.Width;
        height = field.Height;

        _accumulator ??= new LightAccumulator(width, height);
        _accumulator.Reset(width, height, ambient);

        foreach (ILightEmitter emitter in emitters)
        {
            emitter.Emit(field, _accumulator);
        }

        return _accumulator.ToRgba8();
    }
}
