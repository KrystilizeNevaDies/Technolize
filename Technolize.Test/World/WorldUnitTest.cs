using System.Numerics;
using Raylib_cs;
using Technolize.World;
using Technolize.World.Block;
namespace Technolize.Test.World;

[TestFixture]
public class WorldUnitTest
{
    private List<IWorld> _worlds = [];

    [OneTimeSetUp]
    public void GlobalSetup()
    {
        Raylib.InitWindow(1, 1, "World Unit Test");
    }

    [OneTimeTearDown]
    public void GlobalTeardown()
    {
        Raylib.CloseWindow();
    }

    [SetUp]
    public void Setup()
    {
        _worlds =
        [
            new CpuWorld(),
            new GpuTextureWorld()
        ];
    }

    [TearDown]
    public void Teardown()
    {
        foreach (IWorld world in _worlds)
        {
            (world as GpuTextureWorld)?.Unload();
        }
        _worlds.Clear();
    }

    [Test]
    public void CanStoreBlock()
    {
        foreach (IWorld world in _worlds)
        {
            Vector2 pos = new (10, 20);
            world.SetBlock(pos, Blocks.Stone.Id);
            Assert.That(world.GetBlock(pos), Is.EqualTo(Blocks.Stone.Id));
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
                        placer.Set(new (x, y), Blocks.Stone.Id);
                    }
                }

                for (int x = 0; x < 4; x++)
                {
                    placer.Set(new (x, 4), Blocks.Sand.Id);
                }
            });

            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    Assert.That(world.GetBlock(new (x, y)), Is.EqualTo(Blocks.Stone.Id));
                }
                Assert.That(world.GetBlock(new (x, 4)), Is.EqualTo(Blocks.Sand.Id));
            }
        }
    }
}
