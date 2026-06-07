namespace Technolize.Rendering.Lighting;

/// <summary>
/// A light source for the CPU lighting stage. Each emitter is responsible for casting its own rays
/// through the <see cref="CpuLightingField"/> and depositing the light it carries into the shared
/// <see cref="LightAccumulator"/>. Keeping emission behind this interface makes the stage
/// lighting-agnostic: the sun, point lights, emissive blocks, etc. are all just emitters that the stage
/// runs in turn, accumulating additively into the same buffer.
/// </summary>
public interface ILightEmitter
{
    /// <summary>Casts this source's rays through <paramref name="field"/>, depositing into <paramref name="accumulator"/>.</summary>
    void Emit(CpuLightingField field, LightAccumulator accumulator);
}
