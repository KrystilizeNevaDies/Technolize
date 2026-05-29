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
                    Assert.That(world.GetBlock(new (x, y)), Is.EqualTo(Blocks.Stone.Id));
                }
                Assert.That(world.GetBlock(new (x, 4)), Is.EqualTo(Blocks.Sand.Id));
            }
        }
    }

    [Test]
    public void WetStateCreatesDistinctVariantId()
    {
        BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);

        Assert.That(wetDirt.Id, Is.Not.EqualTo(Blocks.Dirt.Id));
        Assert.That(wetDirt.BaseBlock, Is.EqualTo(Blocks.Dirt));
        Assert.That(wetDirt.GetState(CommonBlockStates.Wet), Is.True);
        Assert.That(Blocks.Dirt.GetState(CommonBlockStates.Wet), Is.False);
    }

    [Test]
    public void WetStateVariantCanOverrideDisplayTags()
    {
        BlockInfo wetGrass = Blocks.Grass.WithState(CommonBlockStates.Wet, true);

        Assert.That(wetGrass.GetTag(BlockInfo.TagDisplayName), Is.EqualTo("Wet Grass"));
        Assert.That(wetGrass.GetTag(BlockInfo.TagColor), Is.Not.EqualTo(Blocks.Grass.GetTag(BlockInfo.TagColor)));
    }

    [Test]
    public void CanStoreStatefulBlockInWorld()
    {
        foreach (IWorld world in _worlds)
        {
            Vector2 pos = new(6, 9);
            BlockInfo wetDirt = Blocks.Dirt.WithState(CommonBlockStates.Wet, true);

            world.SetBlock(pos, wetDirt);

            long storedId = world.GetBlock(pos);
            BlockInfo storedBlock = BlockRegistry.GetInfo(storedId);

            Assert.That(storedId, Is.EqualTo(wetDirt.Id));
            Assert.That(storedBlock.BaseBlock, Is.EqualTo(Blocks.Dirt));
            Assert.That(storedBlock.GetState(CommonBlockStates.Wet), Is.True);
        }
    }
}
