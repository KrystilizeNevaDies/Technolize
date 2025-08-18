using System.Numerics;
namespace Technolize.World;

/// <summary>
/// Defines the contract for a world data structure.
/// Provides methods to get, set, and swap blocks at given world coordinates.
/// </summary>
public interface IWorld
{
    /// <summary>
    /// Retrieves the block data at a specific world position.
    /// </summary>
    /// <param name="position">The absolute world coordinates.</param>
    /// <returns>The block data as a long.</returns>
    long GetBlock(Vector2 position);

    /// <summary>
    /// Sets the block data at a specific world position.
    /// </summary>
    /// <param name="position">The absolute world coordinates.</param>
    /// <param name="block">The block data to set.</param>
    void SetBlock(Vector2 position, long block);

    /// <summary>
    /// A helper method to efficiently swap the data of two blocks.
    /// </summary>
    /// <param name="posA">The position of the first block.</param>
    /// <param name="posB">The position of the second block.</param>
    void SwapBlocks(Vector2 posA, Vector2 posB);

    /// <summary>
    /// Retrieves all non-air blocks from the entire world.
    /// </summary>
    /// <param name="min">The inclusive, bottom-left corner of the bounding box. If null, the area has no lower-left boundary.</param>
    /// <param name="max">The exclusive, top-right corner of the bounding box. If null, the area has no upper-right boundary.</param>
    /// <returns>An enumerable collection of tuples, each containing a block's position and its data.</returns>
    IEnumerable<(Vector2 Position, long Block)> GetBlocks(Vector2? min, Vector2? max);
    IEnumerable<(Vector2 Position, long Block)> GetBlocks() => GetBlocks(null, null);

    /// <summary>
    /// Executes a batch operation to set multiple blocks efficiently.
    /// </summary>
    /// <param name="blockPlacerConsumer">An action that receives an IBlockPlacer.
    /// Use the placer's Set method to queue all desired block placements.</param>
    void BatchSetBlocks(Action<IBlockPlacer> blockPlacerConsumer);
}

/// <summary>
/// Defines a contract for an object that can receive block placement commands.
/// This is used by the IWorld.BatchSetBlocks method to collect block data.
/// </summary>
public interface IBlockPlacer
{
    /// <summary>
    /// Queues a block to be set at a specific world position.
    /// </summary>
    void Set(Vector2 position, long block);
}
