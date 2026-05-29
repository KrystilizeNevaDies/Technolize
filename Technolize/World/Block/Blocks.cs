using System.Collections.Frozen;
using Raylib_cs;

namespace Technolize.World.Block;

public static class Blocks
{
    private static uint NextId;

    public static BlockInfo Air { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Air));
        tags.SetTag(BlockInfo.TagColor, new Color(25, 25, 35));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
        tags.SetTag(BlockInfo.TagDensity, 1.225);
    });

    public static BlockInfo Steam { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Steam));
        tags.SetTag(BlockInfo.TagColor, new Color(200, 200, 255));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
        tags.SetTag(BlockInfo.TagDensity, 0.6);
    });

    public static BlockInfo Water { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Water));
        tags.SetTag(BlockInfo.TagColor, new Color(50, 120, 200));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Liquid);
        tags.SetTag(BlockInfo.TagDensity, 1000.0);
        tags.SetTag(BlockTags.Burnable, Steam);
    });

    public static BlockInfo Stone { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Stone));
        tags.SetTag(BlockInfo.TagColor, new Color(130, 135, 140));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
        tags.SetTag(BlockInfo.TagDensity, 2500.0);
    });

    public static BlockInfo Sand { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Sand));
        tags.SetTag(BlockInfo.TagColor, new Color(240, 210, 130));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
        tags.SetTag(BlockInfo.TagDensity, 1200.0);
    });

    public static BlockInfo Bedrock { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Bedrock));
        tags.SetTag(BlockInfo.TagColor, new Color(50, 50, 55));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
        tags.SetTag(BlockInfo.TagDensity, 2800.0);
    });

    public static BlockInfo Fire { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Fire));
        tags.SetTag(BlockInfo.TagColor, new Color(255, 150, 20));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
        tags.SetTag(BlockInfo.TagDensity, 0.3);
    });

    public static BlockInfo Smoke { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Smoke));
        tags.SetTag(BlockInfo.TagColor, new Color(185, 180, 175));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Gas);
        tags.SetTag(BlockInfo.TagDensity, 1.1);
    });

    public static BlockInfo Charcoal { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Charcoal));
        tags.SetTag(BlockInfo.TagColor, new Color(80, 80, 80));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
        tags.SetTag(BlockInfo.TagDensity, 210.0);
        tags.SetTag(BlockTags.Burnable, Air);
    });

    public static BlockInfo Wood { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Wood));
        tags.SetTag(BlockInfo.TagColor, new Color(160, 110, 60));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Solid);
        tags.SetTag(BlockTags.Burnable, Charcoal);
        tags.SetTag(BlockInfo.TagDensity, 750.0);
    });

    public static BlockInfo Dirt { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Dirt));
        tags.SetTag(BlockInfo.TagColor, new Color(150, 105, 75));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
        tags.SetTag(BlockInfo.TagDensity, 1250.0);
    }, states => {
        states.Add(CommonBlockStates.Wet);
    }, (state, tags) => state.Get(CommonBlockStates.Wet)
        ? tags
            .WithTag(BlockInfo.TagDisplayName, "Wet Dirt")
            .WithTag(BlockInfo.TagColor, new Color(108, 80, 62))
        : tags);

    public static BlockInfo Grass { get; } = BlockInfo.Build(ref NextId, tags => {
        tags.SetTag(BlockInfo.TagDisplayName, nameof(Grass));
        tags.SetTag(BlockInfo.TagColor, new Color(100, 180, 60));
        tags.SetTag(BlockInfo.TagMatterState, MatterState.Powder);
        tags.SetTag(BlockTags.Burnable, Smoke);
        tags.SetTag(BlockInfo.TagDensity, 1150.0);
    }, states => {
        states.Add(CommonBlockStates.Wet);
    }, (state, tags) => state.Get(CommonBlockStates.Wet)
        ? tags
            .WithTag(BlockInfo.TagDisplayName, "Wet Grass")
            .WithTag(BlockInfo.TagColor, new Color(72, 138, 56))
        : tags);

    public static FrozenSet<BlockInfo> AllBlocks() {
        return new BlockInfo[] {
            Air,
            Steam,
            Water,
            Stone,
            Sand,
            Bedrock,
            Wood,
            Fire,
            Smoke,
            Charcoal,
            Dirt,
            Grass
        }.ToFrozenSet();
    }

    public static FrozenSet<uint> AllBlockIds() {
        return AllBlocks().Select(block => block.id).ToFrozenSet();
    }
}