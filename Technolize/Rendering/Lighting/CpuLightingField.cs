using System.Numerics;
using Technolize.Utils;
using Technolize.World;
using Technolize.World.Block;

namespace Technolize.Rendering.Lighting;

/// <summary>
/// A dense, per-cell optical copy of the rendered world region used by the CPU lighting stage. It is
/// the lighting equivalent of the colour texture: one entry per cell in texture space (row-major,
/// top-to-bottom, so row 0 is the highest world row), carrying the optical properties a light ray needs
/// to march through the world — the refraction index (for reflection/refraction at material borders)
/// and the per-cell transmission (how much light survives crossing the cell, tinted by the material).
///
/// This is the "copy of the whole world's quadtree" the lighting compute reads, rasterised to a flat
/// grid so a ray march is a simple DDA with O(1) cell lookups. It is rebuilt only when the captured
/// frame or region bounds change; the per-frame ray casting reads it without mutating it.
/// </summary>
public sealed class CpuLightingField
{
    // Per-cell absorption strength for translucent (liquid) materials: the fraction of the gap between
    // white and the material colour mixed into the per-cell transmission. Higher = light is tinted and
    // dimmed faster as it travels through the medium.
    private const float LiquidAbsorptionPerCell = 0.15f;

    private readonly float[] _refraction;
    private readonly Vector3[] _transmission;
    private readonly bool[] _empty;

    private CpuLightingField(int width, int height, int resolution, float[] refraction, Vector3[] transmission, bool[] empty)
    {
        Width = width;
        Height = height;
        Resolution = resolution;
        _refraction = refraction;
        _transmission = transmission;
        _empty = empty;
    }

    /// <summary>Width of the field in light cells (world width times <see cref="Resolution"/>).</summary>
    public int Width { get; }

    /// <summary>Height of the field in light cells (world height times <see cref="Resolution"/>).</summary>
    public int Height { get; }

    /// <summary>Light cells per world cell along each axis (1 = one light cell per world tile).</summary>
    public int Resolution { get; }

    public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Refraction index of the cell, or air's index for out-of-bounds cells.</summary>
    public float RefractionAt(int x, int y) =>
        InBounds(x, y) ? _refraction[(y * Width) + x] : AirRefraction;

    /// <summary>Per-cell light survival fraction (per channel), or fully transparent out of bounds.</summary>
    public Vector3 TransmissionAt(int x, int y) =>
        InBounds(x, y) ? _transmission[(y * Width) + x] : Vector3.One;

    /// <summary>Whether the cell is empty (gas/air): light passes through without being deposited.</summary>
    public bool IsEmpty(int x, int y) => !InBounds(x, y) || _empty[(y * Width) + x];

    /// <summary>Refraction index of air, used for empty space and out-of-bounds cells.</summary>
    public static float AirRefraction { get; } = (float)Blocks.Air.GetTag(BlockInfo.TagRefractionIndex);

    /// <summary>
    /// Rasterises <paramref name="frame"/> into a dense optical field for the region box
    /// <c>[worldRegionStart, worldRegionEnd)</c>. With <paramref name="resolution"/> &gt; 1 each world
    /// cell is subdivided into a <c>resolution x resolution</c> block of light cells (all sharing that
    /// world cell's optics), so the light march and the resulting lighting texture run at a finer grid
    /// than the world for smoother, less blocky illumination.
    /// </summary>
    public static CpuLightingField Build(WorldRenderFrame frame, Vector2 worldRegionStart, Vector2 worldRegionEnd, int resolution = 1)
    {
        resolution = Math.Max(1, resolution);
        int worldWidth = Math.Max(1, (int)((worldRegionEnd.X - worldRegionStart.X) * TickableWorld.RegionSize));
        int worldHeight = Math.Max(1, (int)((worldRegionEnd.Y - worldRegionStart.Y) * TickableWorld.RegionSize));
        int width = worldWidth * resolution;
        int height = worldHeight * resolution;

        float[] refraction = new float[width * height];
        Vector3[] transmission = new Vector3[width * height];
        bool[] empty = new bool[width * height];

        // Default every cell to air optics.
        (float airRefraction, Vector3 airTransmission, bool airEmpty) = GetOptics(Blocks.Air, resolution);
        for (int i = 0; i < refraction.Length; i++)
        {
            refraction[i] = airRefraction;
            transmission[i] = airTransmission;
            empty[i] = airEmpty;
        }

        foreach (WorldRenderRegion region in frame.Regions)
        {
            int regionBaseX = (int)((region.Position.X - worldRegionStart.X) * TickableWorld.RegionSize);
            int regionBaseY = (int)((region.Position.Y - worldRegionStart.Y) * TickableWorld.RegionSize);

            foreach (WorldRenderBlock block in region.Blocks)
            {
                int worldX = regionBaseX + (int)block.LocalPos.X;
                int worldY = regionBaseY + (int)block.LocalPos.Y;
                if (worldX < 0 || worldX >= worldWidth || worldY < 0 || worldY >= worldHeight)
                {
                    continue;
                }

                int textureY = worldHeight - worldY - 1;

                (float r, Vector3 t, bool e) = GetOptics(BlockRegistry.GetInfo(block.BlockId), resolution);

                // Splat this world cell across its resolution x resolution block of light cells.
                int baseX = worldX * resolution;
                int baseY = textureY * resolution;
                for (int sy = 0; sy < resolution; sy++)
                {
                    int rowBase = ((baseY + sy) * width) + baseX;
                    for (int sx = 0; sx < resolution; sx++)
                    {
                        int index = rowBase + sx;
                        refraction[index] = r;
                        transmission[index] = t;
                        empty[index] = e;
                    }
                }
            }
        }

        return new CpuLightingField(width, height, resolution, refraction, transmission, empty);
    }

    /// <summary>
    /// Derives a block's optical properties from its material tags. This is the single place that maps
    /// a material to its light behaviour, so new materials only need the standard colour/refraction/
    /// matter-state tags to participate in lighting:
    /// <list type="bullet">
    ///   <item><description>Gas (air, steam): transparent, light passes through untouched.</description></item>
    ///   <item><description>Liquid (water): translucent, light is attenuated and tinted per cell.</description></item>
    ///   <item><description>Solid/Powder: opaque, light is fully deposited at the surface.</description></item>
    /// </list>
    /// </summary>
    private static (float refraction, Vector3 transmission, bool empty) GetOptics(BlockInfo blockInfo, int resolution)
    {
        float refraction = (float)blockInfo.GetTag(BlockInfo.TagRefractionIndex);
        MatterState state = blockInfo.GetTag(BlockInfo.TagMatterState);

        switch (state)
        {
            case MatterState.Gas:
                return (refraction, Vector3.One, true);

            case MatterState.Liquid:
            {
                // Per-world-tile survival is biased toward the material colour, so light dims and tints
                // as it travels deeper through the medium (e.g. water absorbs red faster than blue).
                // With a resolution multiplier a world tile spans `resolution` light cells, so the
                // per-cell survival is the resolution-th root of the per-tile survival (Beer-Lambert):
                // crossing all `resolution` cells then attenuates exactly like one world tile, keeping
                // the water colour identical at any multiplier.
                Vector3 color = blockInfo.GetTag(BlockInfo.TagColor).ToVector3();
                Vector3 perTile = Vector3.Lerp(Vector3.One, color, LiquidAbsorptionPerCell);
                float invRes = 1f / resolution;
                Vector3 survival = new(
                    MathF.Pow(perTile.X, invRes),
                    MathF.Pow(perTile.Y, invRes),
                    MathF.Pow(perTile.Z, invRes));
                return (refraction, survival, false);
            }

            default:
                // Solid / Powder: opaque. No light passes beyond the surface cell.
                return (refraction, Vector3.Zero, false);
        }
    }
}
