using System.Numerics;
namespace Technolize.World.Generation;

public interface IGenerator {
    void Generate(IUnit unit);
}

/// <summary>
/// Represents a request for the generator to generate blocks.
/// </summary>
public interface IUnit : IPlacer {
    /// <summary>
    /// The minimum (global) position of this generation request.
    /// </summary>
    Vector2 MinPos { get; }

    /// <summary>
    /// The maximum (global) position of this generation request.
    /// </summary>
    Vector2 MaxPos { get; }

    /// <summary>
    /// The size of the generation request.
    /// </summary>
    Vector2 Size { get; }

    /// <summary>
    /// Checks if the unit contains the specified position.
    /// </summary>
    /// <param name="pos">The (global) position to check.</param>
    /// <returns>True if the position is within the bounds of the unit, false otherwise.</returns>
    bool Contains(Vector2 pos) {
        return ContainsX((int)pos.X) && ContainsY((int)pos.Y);
    }

    bool ContainsY(int y) {
        return y >= MinPos.Y && y < MaxPos.Y;
    }

    bool ContainsX(int x) {
        return x >= MinPos.X && x < MaxPos.X;
    }

    // util placements
    void FillY(int minY, int maxY, uint bedrockId) {
        for (int x = (int) MinPos.X; x < MaxPos.X; x++) {
            FillColumn(x, minY, maxY, bedrockId);
        }
    }

    void FillColumn(int x, int minY, int maxY, uint blockId) {
        for (int y = minY; y < maxY; y++) {
            Set(new Vector2(x, y), blockId);
        }
    }
}

public interface IForkedPlacer : IPlacer, IDisposable;
public interface IPlacer {

    /// <summary>
    /// Sets the block at the given position to the specified block ID.
    /// </summary>
    /// <param name="pos">The (global) position.</param>
    /// <param name="blockId">The ID of the block to set at the specified position.</param>
    void Set(Vector2 pos, uint blockId);

    /// <summary>
    /// Forks the current placer to a new (unbound) placer at the specified position.
    /// You will need to close the returned IPlacer to release the placer.
    /// </summary>
    /// <param name="pos">The (global) position to fork the placer at.</param>
    /// <returns>A new IForkedPlacer that is bound to the specified position.</returns>
    IForkedPlacer Fork(Vector2 pos);
    void Fork(Vector2 pos, Action<IPlacer> action) {
        using IForkedPlacer placer = Fork(pos);
        action(placer);
    }
}
