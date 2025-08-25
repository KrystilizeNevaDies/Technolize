using Technolize.Utils;
using Technolize.World.Block;
namespace Technolize.World.Generation.Noise;

public class SimpleNoiseGenerator : IGenerator {

    private readonly FastNoiseLite _noise = new ();
    private readonly FastNoiseLite _white = new ();

    public SimpleNoiseGenerator() {
        _noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        _noise.SetFrequency(0.003);

        // set octavation:
        _noise.SetFractalOctaves(4);
        _noise.SetFractalLacunarity(2.0);
        _noise.SetFractalGain(0.5);
        _noise.SetFractalType(FastNoiseLite.FractalType.FBm);
        _noise.SetSeed(0); // set a fixed seed for reproducibility

        _white.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
        _white.SetFrequency(9867.0);
    }
    public override void Generate(IUnit unit) {
        for (int x = (int) unit.MinPos.X; x < unit.MaxPos.X; x++) {
            double noiseValue = (_noise.GetNoise(0, x) + 1.0) * 0.5;
            double height = 64 + noiseValue * 128;

            double grassNoise = (_noise.GetNoise(1, x * 9578.543) + 1.0) * 0.5;
            int grassHeight = (int)(grassNoise * 4);

            unit.FillColumn(x, 0, (int)height + grassHeight, Blocks.Grass);
            unit.FillColumn(x, 0, (int)height, Blocks.Dirt);
            unit.FillColumn(x, 0, (int)height - 48, Blocks.Stone);
        }

        // layer of bedrock at the bottom
        unit.FillY(0, 4, Blocks.Bedrock);
    }
}
