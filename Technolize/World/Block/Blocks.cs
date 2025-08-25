using System.Collections.Frozen;
using System.Numerics;
using Raylib_cs;
namespace Technolize.World.Block;

public static class Blocks
{
    private static readonly uint NextId;

    public static BlockInfo Air { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Air));
        tags.SetTag(BlockInfo.TagColor, new Color(25, 25, 35));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
    });

    public static BlockInfo Water { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Water));
        tags.SetTag(BlockInfo.TagColor, new Color(50, 120, 200));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Liquid);
    });

    public static BlockInfo Stone { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Stone));
        tags.SetTag(BlockInfo.TagColor, new Color(130, 135, 140));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static BlockInfo Sand { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Sand));
        tags.SetTag(BlockInfo.TagColor, new Color(240, 210, 130));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
    });

    public static BlockInfo Bedrock { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Bedrock));
        tags.SetTag(BlockInfo.TagColor, new Color(50, 50, 55));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static BlockInfo Wood { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Wood));
        tags.SetTag(BlockInfo.TagColor, new Color(160, 110, 60));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static BlockInfo Fire { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Fire));
        tags.SetTag(BlockInfo.TagColor, new Color(255, 150, 20));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
    });

    public static BlockInfo Smoke { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Smoke));
        tags.SetTag(BlockInfo.TagColor, new Color(185, 180, 175));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
    });

    public static BlockInfo Charcoal { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Charcoal));
        tags.SetTag(BlockInfo.TagColor, new Color(80, 80, 80));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
    });

    public static BlockInfo Leaves { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Leaves));
        tags.SetTag(BlockInfo.TagColor, new Color(80, 160, 50));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static BlockInfo Branches { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Branches));
        tags.SetTag(BlockInfo.TagColor, new Color(130, 90, 40));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static BlockInfo Dirt { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Dirt));
        tags.SetTag(BlockInfo.TagColor, new Color(150, 105, 75));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
    });

    public static BlockInfo Grass { get; } = BlockInfo.Build(NextId++, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Grass));
        tags.SetTag(BlockInfo.TagColor, new Color(100, 180, 60));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
    });

    public static FrozenSet<BlockInfo> AllBlocks() {
        return FrozenSet.Create(
            Air,
            Water,
            Stone,
            Sand,
            Bedrock,
            Wood,
            Fire,
            Smoke,
            Charcoal,
            Leaves,
            Branches,
            Dirt,
            Grass
        );
    }

    public static FrozenSet<uint> AllBlockIds() {
        return AllBlocks().Select(block => block.id).ToFrozenSet();
    }
}
