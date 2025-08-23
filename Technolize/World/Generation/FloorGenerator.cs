using System.Numerics;
using Technolize.World.Block;
namespace Technolize.World.Generation;

public class FloorGenerator : IGenerator {

    public override void Generate(IUnit unit) {
        if (!unit.ContainsY(0)) {
            // If the unit does not contain the origin, we don't need to generate a floor.
            return;
        }

        // Place the floor:
        for (int x = (int)unit.MinPos.X; x < unit.MaxPos.X; x++) {
            unit.Set(new Vector2(x, 0), Blocks.Bedrock.Id);
            unit.FillY(0, 16, Blocks.Bedrock.Id);

            if (Random.Shared.NextDouble() < 0.1) {
                // place some sand above the floor
                unit.Fork(new Vector2(x, 1), SandPile);
            }
        }
    }

    private static void SandPile(IPlacer placer) {
        int height = 1 + Random.Shared.Next(0, 3);
        for (int y = 1; y <= height; y++) {
            placer.Set(new Vector2(0, y), Blocks.Sand.Id);
        }
    }
}
