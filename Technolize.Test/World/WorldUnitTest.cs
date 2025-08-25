using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;
namespace Technolize.Test.World;

[TestFixture]
public class WorldUnitTest
{
    private readonly List<IWorld> _worlds = [new TickableWorld()];

    [Test]
    public void CanStoreBlock()
    {
        foreach (IWorld world in _worlds)
        {
            Vector2 pos = new (10, 20);
            world.SetBlock(pos, Blocks.Stone);
            Assert.That(world.GetBlock(pos), Is.EqualTo(Blocks.Stone.id));
        }
    }

    [Test]
    public void CanBatchDraw()
    {
        foreach (IWorld world in _worlds)
        {
            world.BatchSetBlocks(placer =>
            {
                for (int x = 0; x < 4; x++)
                {
                    for (int y = 0; y < 4; y++)
                    {
                        placer.Set(new (x, y), Blocks.Stone);
                    }
                }

                for (int x = 0; x < 4; x++)
                {
                    placer.Set(new (x, 4), Blocks.Sand);
                }
            });

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    Assert.That(world.GetBlock(new (x, y)), Is.EqualTo(Blocks.Stone.id));
                }
                Assert.That(world.GetBlock(new (x, 4)), Is.EqualTo(Blocks.Sand.id));
            }
        }
    }
}
