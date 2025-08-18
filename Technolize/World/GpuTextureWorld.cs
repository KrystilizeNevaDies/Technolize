using System.Numerics;
using Raylib_cs;
using Technolize.World.Block;
namespace Technolize.World;

/// <summary>
/// An IWorld implementation where the world's data is stored directly in GPU textures.
/// </summary>
public class GpuTextureWorld : IWorld
{
    public const int RegionSize = 256;
    public readonly Dictionary<Vector2, RenderTexture2D> Regions = new();

    // --- Helper Methods for Data Conversion ---

    private static Color BlockIdToColor(long block)
    {
        return BlockRegistry.GetInfo(block).Color;
    }

    private long ColorToBlockId(Color color)
    {
        return BlockRegistry.GetInfoByColor(color).Id;
    }

    private static (Vector2 regionPos, Vector2 localPos) WorldToRegionCoords(Vector2 worldPos)
    {
        int regionX = (int)Math.Floor(worldPos.X / RegionSize);
        int regionY = (int)Math.Floor(worldPos.Y / RegionSize);

        int localX = (int)worldPos.X % RegionSize;
        if (localX < 0) localX += RegionSize;

        int localY = (int)worldPos.Y % RegionSize;
        if (localY < 0) localY += RegionSize;

        return (new Vector2(regionX, regionY), new Vector2(localX, localY));
    }

    public long GetBlock(Vector2 position)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        if (!Regions.TryGetValue(regionPos, out RenderTexture2D regionTexture))
        {
            // If the region doesn't exist on the GPU, it's all Air.
            return Blocks.Air.Id;
        }

        Image regionImage = Raylib.LoadImageFromTexture(regionTexture.Texture);

        // Apply the Y-flip correction
        int flippedY = RegionSize - 1 - (int)localPos.Y;
        Color pixelColor = Raylib.GetImageColor(regionImage, (int)localPos.X, flippedY);
        Raylib.UnloadImage(regionImage);

        return ColorToBlockId(pixelColor);
    }

    public void SetBlock(Vector2 position, long block)
    {
        (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

        // If setting to Air in a region that doesn't exist, do nothing.
        if (block == Blocks.Air.Id && !Regions.ContainsKey(regionPos))
        {
            return;
        }

        // Get the region texture, creating it if it doesn't exist.
        if (!Regions.TryGetValue(regionPos, out RenderTexture2D regionTexture))
        {
            // Create a new render texture on the GPU. New textures are black (0), which is Air.
            regionTexture = Raylib.LoadRenderTexture(RegionSize, RegionSize);
            Regions[regionPos] = regionTexture;
        }

        // Bind the texture as a render target and issue a draw call for a single pixel.
        Raylib.BeginTextureMode(regionTexture);
        DrawPixel(localPos, block);
        Raylib.EndTextureMode();
    }

    /// <summary>
    /// Swaps two blocks. Optimized for the case where both blocks are in the same region
    /// by reducing the number of GPU-to-CPU data transfers from two to one.
    /// </summary>
    public void SwapBlocks(Vector2 posA, Vector2 posB)
    {
        (Vector2 regionPosA, Vector2 localPosA) = WorldToRegionCoords(posA);
        (Vector2 regionPosB, Vector2 localPosB) = WorldToRegionCoords(posB);

        if (regionPosA != regionPosB)
        {
            // We must read both blocks first before writing, so a multistep process is required.
            long blockA = GetBlock(posA);
            long blockB = GetBlock(posB);

            SetBlock(posA, blockB);
            SetBlock(posB, blockA);
            return;
        }

        if (!Regions.TryGetValue(regionPosA, out RenderTexture2D regionTexture))
        {
            return;
        }

        Image regionImage = Raylib.LoadImageFromTexture(regionTexture.Texture);
        Color colorA, colorB;

        try
        {
            // apply y-flip correction for the image
            int flippedYPosA = RegionSize - 1 - (int)localPosA.Y;
            int flippedYPosB = RegionSize - 1 - (int)localPosB.Y;
            colorA = Raylib.GetImageColor(regionImage, (int)localPosA.X, flippedYPosA);
            colorB = Raylib.GetImageColor(regionImage, (int)localPosB.X, flippedYPosB);
        }
        finally
        {
            Raylib.UnloadImage(regionImage);
        }

        // Write both pixels back to the GPU texture in one drawing session.
        Raylib.BeginTextureMode(regionTexture);
        DrawPixel(localPosA, ColorToBlockId(colorB));
        DrawPixel(localPosB, ColorToBlockId(colorA));
        Raylib.EndTextureMode();
    }

    public IEnumerable<(Vector2 Position, long Block)> GetBlocks(Vector2? min, Vector2? max)
    {
        List<Vector2> regionPositions = new List<Vector2>(Regions.Keys);

        foreach (Vector2 regionPos in regionPositions)
        {
            float regionMinX = regionPos.X * RegionSize;
            float regionMinY = regionPos.Y * RegionSize;
            float regionMaxX = regionMinX + RegionSize - 1;
            float regionMaxY = regionMinY + RegionSize - 1;

            if (min.HasValue && (regionMaxX < min.Value.X || regionMaxY < min.Value.Y))
            {
                continue;
            }
            if (max.HasValue && (regionMinX >= max.Value.X || regionMinY >= max.Value.Y))
            {
                continue;
            }

            if (!Regions.TryGetValue(regionPos, out RenderTexture2D regionTexture)) continue;

            Image regionImage = Raylib.LoadImageFromTexture(regionTexture.Texture);

            try
            {
                for (int y = 0; y < RegionSize; y++)
                {
                    for (int x = 0; x < RegionSize; x++)
                    {
                        Vector2 worldPos = new Vector2(regionMinX + x, regionMinY + y);

                        if (min.HasValue && (worldPos.X < min.Value.X || worldPos.Y < min.Value.Y))
                        {
                            continue;
                        }
                        if (max.HasValue && (worldPos.X >= max.Value.X || worldPos.Y >= max.Value.Y))
                        {
                            continue;
                        }

                        // apply y-flip correction for the image
                        int flippedY = RegionSize - 1 - y;

                        Color pixelColor = Raylib.GetImageColor(regionImage, x, flippedY);
                        long block = ColorToBlockId(pixelColor);

                        if (block == Blocks.Air.Id) continue;

                        yield return (worldPos, block);
                    }
                }
            }
            finally
            {
                Raylib.UnloadImage(regionImage);
            }
        }
    }

    /// <summary>
    /// A private helper class that implements IBlockPlacer to collect and group
    /// block placements by region before they are sent to the GPU.
    /// </summary>
    private class BatchPlacer : IBlockPlacer
    {

        // The collected data: A dictionary mapping a region's position to a list of
        // all local positions and block data to be placed within that region.
        public readonly Dictionary<Vector2, List<(Vector2 localPos, long block)>> PendingBlocks = new();

        public void Set(Vector2 position, long block)
        {
            (Vector2 regionPos, Vector2 localPos) = WorldToRegionCoords(position);

            // Find the list of placements for this region, or create it if it doesn't exist.
            if (!PendingBlocks.TryGetValue(regionPos, out List<(Vector2 localPos, long block)>? placements))
            {
                placements = [];
                PendingBlocks[regionPos] = placements;
            }

            // Add the block placement command to the list for this region.
            placements.Add((localPos, block));
        }
    }

    /// <summary>
    /// Executes a batch operation to set multiple blocks. This is highly efficient as it groups
    /// all draw calls for a single region into one operation, minimizing GPU state changes.
    /// </summary>
    public void BatchSetBlocks(Action<IBlockPlacer> blockPlacerConsumer)
    {
        BatchPlacer placer = new BatchPlacer();

        blockPlacerConsumer(placer);

        foreach ((Vector2 regionPos, List<(Vector2 localPos, long block)> placements) in placer.PendingBlocks)
        {
            // Get the render texture for the region, creating it if it's new.
            if (!Regions.TryGetValue(regionPos, out RenderTexture2D regionTexture))
            {
                regionTexture = Raylib.LoadRenderTexture(RegionSize, RegionSize);
                Regions[regionPos] = regionTexture;
            }

            Raylib.BeginTextureMode(regionTexture);

            // Execute all queued draw calls for this region.
            foreach ((Vector2 localPos, long block) in placements)
            {
                DrawPixel(localPos, block);
            }

            Raylib.EndTextureMode();
        }
    }

    /// <summary>
    /// Cleans up all GPU resources (VRAM) used by this world instance.
    /// Must be called when the world is no longer needed.
    /// </summary>
    public void Unload()
    {
        foreach (RenderTexture2D regionTexture in Regions.Values)
        {
            Raylib.UnloadRenderTexture(regionTexture);
        }
        Regions.Clear();
    }

    private void DrawPixel(Vector2 pos, long block)
    {
        Raylib.DrawPixel((int)pos.X, (int)pos.Y, block == Blocks.Air.Id ? new Color(0, 0, 0, 255) : BlockIdToColor(block));
    }
}
